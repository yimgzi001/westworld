"""Pure public JSON decoding only. No game, driver run loop, network or model."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent

def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

food = load(HERE / 'deterministic_driver.py', 'food_decoder_test')
natural = load(HERE.parent / 'natural-resource-evidence/deterministic_driver.py', 'natural_decoder_test')

def table(rows):
    columns = sorted(rows[0])
    return dict(encoding='lossless_columns_v1', columns=columns,
                rows=[[row[c] for c in columns] for row in rows], rowCount=len(rows))

class DecoderTests(unittest.TestCase):
    def setUp(self):
        self.rows = [dict(reference='ref_a', value=2, face=None, cell=dict(x=1,y=64,z=2)),
                     dict(reference='ref_b', value=9, face=1, cell=dict(x=2,y=64,z=2))]
        self.original = dict(observation=dict(visibleBlocks=self.rows,
            entities=[], pickables=[dict(reference='drop1', sense='vision', count=1)],
            coverage=dict(scanComplete=False, omittedBlocks=7, pickablesTruncated=True,
                          objectRange=16)), inventory=dict(slots=[]))
        self.encoded = copy.deepcopy(self.original)
        self.encoded['observation']['visibleBlocks'] = table(self.rows)

    def test_round_trip_and_raw_immutable(self):
        before = copy.deepcopy(self.encoded)
        self.assertEqual(food.decode_public_facts(self.encoded), self.original)
        self.assertEqual(self.encoded, before)

    def test_plain_list_unchanged(self):
        self.assertIs(food.decode_public_facts(self.original), self.original)

    def test_both_consumers_decode_final_model_text(self):
        request = dict(prompt='TOOLS\nOBSERVATION:\n' + json.dumps(dict(
            projectionStatus='projected', facts=self.encoded, recentResults=[])))
        self.assertEqual(food.facts_from_prompt(request), self.original)
        self.assertEqual(natural.shared.facts_from_prompt(request), self.original)

    def test_all_public_arrays(self):
        expected = copy.deepcopy(self.original)
        expected['observation']['entities'] = self.rows
        expected['observation']['pickables'] = self.rows
        encoded = copy.deepcopy(expected)
        for key in ('visibleBlocks', 'entities', 'pickables'):
            encoded['observation'][key] = table(self.rows)
        self.assertEqual(food.decode_public_facts(encoded), expected)

    def test_missing_and_null_preserved(self):
        rows = [dict(reference='a', face=None), dict(reference='b', face=None)]
        result = food.decode_public_facts(dict(observation=dict(visibleBlocks=table(rows))))
        self.assertIsNone(result['observation']['visibleBlocks'][0]['face'])
        self.assertNotIn('value', result['observation']['visibleBlocks'][0])

    def test_malformed_tables_rejected(self):
        changes = [dict(columns=['reference','reference']), dict(columns=[]),
                   dict(columns=['reference',None]), dict(rows=[[]]),
                   dict(rowCount=True), dict(rowCount=-1), dict(rowCount=3),
                   dict(encoding='future_table'), dict(extra='ignored?'),
                   dict(rows=[{},{}])]
        for changeset in changes:
            with self.subTest(changes=changeset):
                changed = copy.deepcopy(self.encoded)
                changed['observation']['visibleBlocks'].update(changeset)
                with self.assertRaises(ValueError):
                    food.decode_public_facts(changed)

    def test_real_c_snapshot_encoding_roundtrip(self):
        path = HERE.parent.parent / 'tests/Westworld.ToolResultProjection.Tests/observation40-c.json'
        snapshot = json.loads(path.read_text())
        facts = dict(observation=snapshot)
        encoded = copy.deepcopy(facts)
        rows = snapshot['visibleBlocks']
        self.assertTrue(rows)
        self.assertTrue(all(set(r)==set(rows[0]) for r in rows))
        encoded['observation']['visibleBlocks'] = table(rows)
        self.assertEqual(food.decode_public_facts(encoded), facts)

if __name__ == '__main__':
    unittest.main()
