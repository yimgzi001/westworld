"""仅测试可读表的无损重建及拒绝坏输入，不运行游戏/API。"""
import json,unittest
from receipt_table import encode_log,decode_log
ROW='{"kind":"tool_receipt","receipt":{"Sequence":6,"Tool":"host_navigation_status","Data":{"elapsed_seconds":0,"path":1.20e-3,"readiness":{"Health":1},"start_failure_readiness":{"Health":0.50}}},"arguments":{}}\n'
class Cases(unittest.TestCase):
 def check_roundtrip(self,t):
  a,b,c=encode_log(t);self.assertEqual(decode_log(a,b),t);return a,b,c
 def test_empty(self):self.check_roundtrip('')
 def test_one(self):self.check_roundtrip(ROW)
 def test_number_lexemes_and_duplicate_names(self):self.check_roundtrip(ROW+ROW.replace('Sequence":6','Sequence":7').replace('1.20e-3','-2.0E+2').replace('0.50','0.00'))
 def test_non_receipt_verbatim(self):self.check_roundtrip('{"kind":"failure","message":"失败\\n原文"}\n'+ROW)
 def test_other_tools_unchanged(self):a,b,c=self.check_roundtrip(ROW.replace('host_navigation_status','act_Eat'));self.assertEqual(c['navigationStatusRows'],0)
 def test_boundary_new_template(self):a,b,c=self.check_roundtrip(ROW+ROW.replace('arguments":{}','arguments":{"x":true}'));self.assertEqual(c['templateCount'],2)
 def test_257_rows_split(self):a,b,c=self.check_roundtrip(ROW*257);self.assertEqual(c['streamRecords'],2)
 def test_bad_template_rejected(self):
  with self.assertRaises(AssertionError):decode_log(ROW,'{"_receipt_table":99,"columns":[],"rows":[[]]}\n')
 def test_bad_number_rejected(self):
  with self.assertRaises(AssertionError):decode_log(ROW,'{"_receipt_table":0,"columns":["Sequence"],"rows":[["NaN"]]}\n')
 def test_reserved_key_rejected(self):
  with self.assertRaises(AssertionError):encode_log('{"_receipt_table":0}\n')
if __name__=='__main__':unittest.main()
