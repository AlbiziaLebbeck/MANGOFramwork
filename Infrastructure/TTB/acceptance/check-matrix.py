"""Trace every planned requirement/case to actual local results; never infer bank acceptance."""
import argparse
import datetime as dt
import hashlib
import json
import pathlib
import re
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[3]
MANIFEST = pathlib.Path(__file__).with_name('matrix.json')

def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def planned_rows(text, prefix):
    result = {}
    for line in text.splitlines():
        match = re.match(r'\| (' + prefix + r'\d{2}) \|', line)
        if match:
            cells = [cell.strip() for cell in line.strip('|').split('|')]
            if match[1] in result:
                raise ValueError('Duplicate planned ID: ' + match[1])
            result[match[1]] = cells[1:]
    return result

def trx_results(paths):
    latest, sources = {}, []
    namespace = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    for path in sorted(paths):
        if not path.resolve().is_relative_to((ROOT / '.artifacts/test-results').resolve()):
            raise ValueError('TRX input must be an existing workspace test artifact.')
        run = ET.parse(path).getroot()
        times = run.find('t:Times', namespace)
        if times is None or not times.get('finish'):
            raise ValueError('A completed TRX timestamp is required.')
        finished = dt.datetime.fromisoformat(times.get('finish').replace('Z', '+00:00'))
        records = run.findall('t:Results/t:UnitTestResult', namespace)
        if not records:
            raise ValueError('An empty TRX cannot prove coverage.')
        for result in records:
            name = result.get('testName')
            if name in latest and finished == latest[name]['finished'] and result.get('outcome') != latest[name]['outcome']:
                raise ValueError('Conflicting outcomes have the same timestamp; preserve and review both runs.')
            if name not in latest or finished > latest[name]['finished']:
                latest[name] = {'outcome': result.get('outcome'), 'finished': finished, 'source': str(path.relative_to(ROOT))}
        sources.append({'path': str(path.relative_to(ROOT)), 'sha256': sha(path), 'finishedAt': finished.isoformat(), 'tests': len(records)})
    return latest, sources

def browser_results(configuration):
    result = {}
    for name, suite in configuration.items():
        path = ROOT / suite['path']
        if not path.is_file():
            result[name] = {'verified': False, 'reason': 'not_run', 'tests': 0, 'path': suite['path']}
            continue
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        tests, files = [], set()
        def visit(node):
            if node.get('file'):
                files.add(pathlib.Path(node['file']).name)
            for spec in node.get('specs', []):
                for test in spec.get('tests', []):
                    runs = test.get('results', [])
                    passed = test.get('expectedStatus') == 'passed' and bool(runs) and all(run.get('status') == 'passed' for run in runs)
                    tests.append({'title': spec['title'], 'passed': passed})
            for child in node.get('suites', []):
                visit(child)
        visit(data)
        config_name = pathlib.Path(data.get('config', {}).get('configFile', '').replace('\\', '/')).name
        identity_valid = config_name == suite['configFile'] and set(suite['files']).issubset(files)
        valid = identity_valid and len(tests) >= suite['minimumTests'] and all(test['passed'] for test in tests) and not data.get('errors') and bool(data.get('stats', {}).get('startTime'))
        result[name] = {'verified': valid, 'tests': len(tests), 'path': suite['path'], 'sha256': sha(path),
                        'suiteIdentityVerified': identity_valid,
                        'startedAt': data.get('stats', {}).get('startTime'), 'durationMs': data.get('stats', {}).get('duration'), 'results': tests}
    return result

def evaluate(manifest, plan, results, suites):
    requirements, planned = planned_rows(plan, 'R'), planned_rows(plan, 'T')
    if set(requirements) != {f'R{i:02}' for i in range(1, 27)} or set(planned) != {f'T{i:02}' for i in range(1, 47)}:
        raise ValueError('The plan changed; review the requirement/case baseline before regenerating.')
    if set(manifest['cases']) != set(planned):
        raise ValueError('Every planned T case must have exactly one mapping.')
    cases = {}
    for identifier, mapping in manifest['cases'].items():
        if not mapping.get('tests') and not mapping.get('suites'):
            raise ValueError(identifier + ' has no local evidence mapping.')
        missing, failures, matched = [], [], set()
        for selector in mapping.get('tests', []):
            if not re.fullmatch(r'[A-Za-z]+Tests\.[A-Za-z0-9_]+', selector):
                raise ValueError('Use an exact class/method selector, without wildcards: ' + identifier)
            prefix = 'TtbCommerce.Tests.' + selector
            names = [name for name in results if name == prefix or name.startswith(prefix + '(')]
            if not names:
                missing.append(selector)
            matched.update(names)
            failures.extend(name for name in names if results[name]['outcome'] != 'Passed')
        needed_suites = mapping.get('suites', [])
        if any(name not in suites for name in needed_suites):
            raise ValueError('Unknown browser suite: ' + identifier)
        gates = mapping.get('gates', [])
        if len(set(gates)) != len(gates) or any(name not in manifest['gates'] for name in gates):
            raise ValueError('Unknown/duplicate acceptance gate: ' + identifier)
        linked = re.findall(r'R\d{2}', planned[identifier][-1])
        if not linked or any(name not in requirements for name in linked):
            raise ValueError('Invalid requirement linkage: ' + identifier)
        verified = not missing and not failures and all(suites[name]['verified'] for name in needed_suites)
        cases[identifier] = {'scenario': planned[identifier][0], 'expected': planned[identifier][1], 'requirements': linked,
            'localVerified': verified, 'localTestCount': len(matched), 'testSelectors': mapping.get('tests', []),
            'missingTests': missing, 'nonPassingTests': failures, 'suites': needed_suites, 'pendingAcceptanceGates': gates,
            'acceptanceComplete': verified and not gates}
    requirement_rows = {}
    for identifier, cells in requirements.items():
        linked = [key for key, row in cases.items() if identifier in row['requirements']]
        if not linked:
            raise ValueError('Unmapped requirement: ' + identifier)
        requirement_rows[identifier] = {'description': cells[0], 'criteria': cells[1], 'cases': linked,
            'localVerified': all(cases[key]['localVerified'] for key in linked),
            'acceptanceComplete': all(cases[key]['acceptanceComplete'] for key in linked)}
    return cases, requirement_rows

def markdown(report):
    lines = ['# TTB requirement/evidence matrix', '', 'Generated from the plan, explicit selectors and completed TRX/Playwright JSON. Local verification does not certify TTB, company/device acceptance or Production.', '',
             f"Generated UTC: `{report['generatedAt']}`. Local cases verified: **{report['localCasesVerified']}/46**; requirements traced: **26/26**. Final acceptance complete: **{report['acceptedCases']}/46**.", '',
             '## Requirements', '', '| ID | Requirement | Cases | Local evidence | Final acceptance |', '|---|---|---|---|---|']
    for key, row in report['requirements'].items():
        lines.append(f"| {key} | {row['description']} | {', '.join(row['cases'])} | {'PASS' if row['localVerified'] else 'INCOMPLETE'} | {'COMPLETE' if row['acceptanceComplete'] else 'PENDING'} |")
    lines += ['', '## Test cases', '', '| ID | Scenario | Local result | Backend cases | Browser suites | Outstanding acceptance |', '|---|---|---|---:|---|---|']
    for key, row in report['cases'].items():
        lines.append(f"| {key} | {row['scenario']} | {'PASS' if row['localVerified'] else 'INCOMPLETE'} | {row['localTestCount']} | {', '.join(row['suites']) or '—'} | {', '.join(row['pendingAcceptanceGates']) or '—'} |")
    lines += ['', '## Pending acceptance gates', '']
    for key, description in report['gates'].items():
        lines.append(f'- **{key}**: {description}. Status: pending; no inferred approval.')
    lines += ['', '## Evidence and reproduction', '', 'Mappings: `Infrastructure/TTB/acceptance/matrix.json`. Checker: `check-matrix.py --trx <completed TRX> [<additional TRX> ...]`. Latest outcome wins for each exact parameterized test; an older pass cannot mask a newer failure. Every selector must match actual tests. Missing/failed/skipped results and incomplete browser suites make the local matrix incomplete.', '',
              'Sanitized machine report: `.artifacts/acceptance/requirement-matrix.json` includes artifact SHA256/timestamps and exact selectors. Run the documented PostgreSQL test wrapper and the five Playwright configurations to reproduce. The bounded Local Mock load experiment is not a bank-callback or production-capacity benchmark. Source and build artifacts are separate from bank/device/operations acceptance. No bank credential, private data or fabricated external acceptance is stored in this report.', '']
    for source in report['trxSources']:
        lines.append(f"- `{source['path']}` — {source['tests']} results; SHA256 `{source['sha256']}`.")
    return '\n'.join(lines) + '\n'

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--trx', type=pathlib.Path, nargs='+', required=True)
    args = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8-sig'))
    results, sources = trx_results([path.resolve() for path in args.trx])
    suites = browser_results(manifest['suites'])
    plan = (ROOT / manifest['plan']).read_text(encoding='utf-8-sig')
    cases, requirements = evaluate(manifest, plan, results, suites)
    nonpassing = [name for name, result in results.items() if result['outcome'] != 'Passed']
    report = {'generatedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'manifestSha256': sha(MANIFEST), 'planSha256': sha(ROOT / manifest['plan']),
              'trxSources': sources, 'suites': suites, 'gates': manifest['gates'], 'cases': cases, 'requirements': requirements,
              'localCasesVerified': sum(row['localVerified'] for row in cases.values()), 'acceptedCases': sum(row['acceptanceComplete'] for row in cases.values()),
              'backendDistinctResults': len(results), 'backendNonPassing': nonpassing,
              'bankAccepted': False, 'productionAccepted': False}
    output = ROOT / '.artifacts/acceptance'; output.mkdir(parents=True, exist_ok=True)
    (output / 'requirement-matrix.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'Documentation/TTB_REQUIREMENT_MATRIX.md').write_text(markdown(report), encoding='utf-8')
    incomplete = {key: {'missingTests': row['missingTests'], 'nonPassingTests': row['nonPassingTests'],
                       'incompleteSuites': [name for name in row['suites'] if not suites[name]['verified']]}
                  for key, row in cases.items() if not row['localVerified']}
    print(json.dumps({'localCasesVerified': report['localCasesVerified'], 'totalCases': 46, 'requirementsTraced': len(requirements),
                      'backendDistinctResults': len(results), 'backendNonPassing': nonpassing,
                      'incomplete': incomplete, 'bankAccepted': False, 'productionAccepted': False}, ensure_ascii=False))
    if incomplete or nonpassing:
        raise SystemExit(1)

if __name__ == '__main__':
    main()
