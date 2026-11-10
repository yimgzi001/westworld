import unittest
from summarize import summarize
class Tests(unittest.TestCase):
 def test_requires_outer(self):self.assertFalse(summarize([{'kind':'matrix40_summary','passed':True}],'matrix_single')['passed'])
 def test_success(self):self.assertTrue(summarize([{'kind':'matrix40_summary','passed':True},{'kind':'result','passed':True}],'matrix_single')['passed'])
 def test_assert_failure(self):self.assertFalse(summarize([{'kind':'matrix40_summary','passed':True},{'kind':'result','passed':True},{'kind':'matrix40_assertion','passed':False}],'matrix_single')['passed'])
 def test_cleanup_failure(self):self.assertFalse(summarize([{'kind':'matrix40_social_result','passed':True},{'kind':'result','passed':True},{'kind':'cleanup_failure'}],'matrix_social')['passed'])
 def test_native_not_adapter(self):self.assertEqual(summarize([{'kind':'matrix40_receipt','tool':'act_Speak'}],'matrix_social')['adapterValidatedTools'],[])
 def test_dedup_adapter(self):self.assertEqual(summarize([{'kind':'matrix40_adapter_validated','tool':'act_Wait'}]*2,'matrix_single')['adapterValidatedTools'],['act_Wait'])
if __name__=='__main__':unittest.main()
