import copy
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
SCRIPT = ROOT / 'Infrastructure/TTB/acceptance/check-matrix.py'
spec = importlib.util.spec_from_file_location('matrix', SCRIPT)
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)

class AcceptanceEvidenceTests(unittest.TestCase):
    def write_trx(self, directory, name, outcome, timestamp):
        path = pathlib.Path(directory) / name
        path.write_text(f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
        <Times finish="{timestamp}"/><Results><UnitTestResult testName="TtbCommerce.Tests.QrTests.Bank_golden_payload_and_CRC_match_the_document_exactly" outcome="{outcome}"/></Results></TestRun>''', encoding='utf-8')
        return path

    def test_a_newer_failed_run_is_never_hidden_by_an_older_pass_or_input_order(self):
        parent = ROOT / '.artifacts/test-results'; parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='ttb-matrix-test-', dir=parent) as directory:
            older = self.write_trx(directory, 'z-old.trx', 'Passed', '2026-10-19T00:00:00Z')
            newer = self.write_trx(directory, 'a-new.trx', 'Failed', '2026-10-19T01:00:00Z')
            for paths in ([older, newer], [newer, older]):
                results, _ = matrix.trx_results(paths)
                self.assertEqual('Failed', next(iter(results.values()))['outcome'])

    def test_ambiguous_same_timestamp_outcomes_are_rejected(self):
        parent = ROOT / '.artifacts/test-results'; parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='ttb-matrix-test-', dir=parent) as directory:
            first = self.write_trx(directory, 'one.trx', 'Passed', '2026-10-19T00:00:00Z')
            other = self.write_trx(directory, 'two.trx', 'Failed', '2026-10-19T00:00:00Z')
            with self.assertRaises(ValueError):
                matrix.trx_results([first, other])

    def test_a_green_shop_report_cannot_be_substituted_for_compiled_Unity_evidence(self):
        parent = ROOT / '.artifacts/acceptance'; parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='ttb-matrix-test-', dir=parent) as directory:
            path = pathlib.Path(directory) / 'report.json'
            path.write_text(json.dumps({'config': {'configFile': 'playwright.config.ts'}, 'stats': {'startTime': '2026-10-19T00:00:00Z'},
                'suites': [{'file': 'gallery.spec.ts', 'specs': [{'title': 'green but wrong runtime', 'tests': [{'expectedStatus': 'passed', 'results': [{'status': 'passed'}]}]}]}]}), encoding='utf-8')
            result = matrix.browser_results({'unity': {'path': str(path.relative_to(ROOT)), 'minimumTests': 1,
                'configFile': 'playwright.unity.config.ts', 'files': ['gallery.spec.ts']}})
            self.assertFalse(result['unity']['verified'])
            self.assertFalse(result['unity']['suiteIdentityVerified'])

    def test_missing_mappings_are_rejected_and_an_unrun_selector_cannot_pass(self):
        manifest = json.loads(matrix.MANIFEST.read_text(encoding='utf-8'))
        plan = (ROOT / manifest['plan']).read_text(encoding='utf-8')
        suites = {name: {'verified': True} for name in manifest['suites']}
        missing = copy.deepcopy(manifest); del missing['cases']['T01']
        with self.assertRaises(ValueError):
            matrix.evaluate(missing, plan, {}, suites)
        cases, _ = matrix.evaluate(manifest, plan, {}, suites)
        self.assertFalse(cases['T01']['localVerified'])
        self.assertTrue(cases['T01']['missingTests'])
        self.assertFalse(cases['T01']['acceptanceComplete'])

if __name__ == '__main__':
    unittest.main()
