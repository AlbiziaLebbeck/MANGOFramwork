import importlib.util
import pathlib
import unittest
import uuid

REPO = pathlib.Path(__file__).resolve().parents[3]
SCRIPT = REPO / 'Infrastructure' / 'TTB' / 'uat' / 'compare-restore-data.py'
spec = importlib.util.spec_from_file_location('comparison', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class RestoreComparisonTests(unittest.TestCase):
    def export(self, rows=('a\tPaid', 'b\tRefunded'), extra='', columns='"Id", "PaymentState"'):
        root = REPO / '.artifacts' / 'ttb-recovery' / ('comparison-test-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        path = root / 'data.sql'
        with path.open('w', encoding='utf-8', newline='\n') as output:
            for table in ['Orders', 'BankTransactions', 'Allocations', 'Jobs']:
                output.write(f'COPY ttb."{table}" ({columns}) FROM stdin;\n')
                if table == 'Orders':
                    for row in rows:
                        output.write(row + '\n')
                output.write('\\.\n')
            output.write(extra)
        return path

    def test_row_order_is_irrelevant_but_every_column_and_row_is_preserved(self):
        original = module.inspect(self.export())
        reordered = module.inspect(self.export(rows=('b\tRefunded', 'a\tPaid')))
        self.assertEqual(original, reordered)
        self.assertEqual(2, original['tables']['ttb.Orders']['rows'])
        self.assertEqual(0, original['tables']['ttb.Jobs']['rows'])

    def test_changed_money_state_missing_row_and_duplicate_row_are_detected(self):
        original = module.inspect(self.export())
        for rows in [('a\tPaid', 'b\tPaid'), ('a\tPaid',), ('a\tPaid', 'b\tRefunded', 'b\tRefunded')]:
            with self.subTest(rows=len(rows)):
                self.assertNotEqual(original, module.inspect(self.export(rows=rows)))

    def test_column_order_and_sequence_state_are_not_ignored(self):
        original = module.inspect(self.export())
        self.assertNotEqual(original, module.inspect(self.export(columns='"PaymentState", "Id"')))
        self.assertNotEqual(original, module.inspect(self.export(extra="SELECT pg_catalog.setval('ttb.seq', 42, true);\n")))

    def test_unterminated_duplicate_and_unknown_copy_syntax_are_rejected(self):
        for extra in ['COPY ttb."NeverTerminated" ("Id") FROM stdin;\n', 'COPY ttb."Jobs" ("Id") FROM stdin;\n\\.\n', 'COPY malicious FROM PROGRAM xyz;\n']:
            with self.subTest(kind=extra[:25]):
                with self.assertRaises(ValueError):
                    module.inspect(self.export(extra=extra))

    def test_copy_escaped_newlines_and_literal_backslashes_are_hashed_as_data(self):
        result = module.inspect(self.export(rows=('a\tline\\nnext', 'b\t\\\\.')))
        self.assertEqual(2, result['tables']['ttb.Orders']['rows'])

if __name__ == '__main__':
    unittest.main()
