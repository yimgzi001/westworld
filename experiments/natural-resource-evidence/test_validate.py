import unittest,json,pathlib,copy
import validate
class Tests(unittest.TestCase):
 def data(self):return json.loads((pathlib.Path(__file__).parent/'synthetic-schema.json').read_text())['example']
 def test_synthetic_pass_not_native(self):self.assertEqual(validate.validate(self.data())['verdict'],'pass')
 def test_corruptions(self):
  cases=[lambda x:x.update(aiViewportCount=1),lambda x:x.update(actorGameWidgetNull=False),lambda x:x['fixture'].update(inventoryInjected=True),lambda x:x['ledger'].update(Unknown=True),lambda x:x.update(origins=[]),lambda x:x.update(transfers=[]),lambda x:x['attempts'][0].update(TerminalStatus='running'),lambda x:x['attempts'][0].update(RunId=None),lambda x:x['origins'][0]['fact'].update(CellX=99),lambda x:x['transfers'][0]['fact'].update(InventoryAfter=3),lambda x:x['actualInventory'].update({'2':2}),lambda x:x['shutdown'].update(lateDispatches=1),lambda x:x['auditFailures'].update(originRecordAfter=1),lambda x:x['origins'][0]['fact'].update(MonotonicSeconds=99),lambda x:x['shutdown'].update(drainedTime=120)]
  for i,change in enumerate(cases):
   with self.subTest(case=i):
    x=self.data();change(x);self.assertEqual(validate.validate(x)['verdict'],'fail')
 def test_raw_receipts_required(self):
  e=self.data();a=e['attempts'][0];o=e['origins'][0];t=e['transfers'][0]
  rows=[e,dict(kind='result',passed=True,updateFailures=0),dict(kind='natural_origin_bound',**o),dict(kind='natural_transfer',**t),dict(kind='native_call_returned',sequence=a['ReceiptSequence'],tool='act_Dig',data=dict(runId=a['RunId'])),dict(kind='tool_receipt',receipt=dict(Tool='action_status',Data=dict(runId=a['RunId'],status='succeeded')))]
  self.assertEqual(validate.validate_log(rows)['verdict'],'pass')
  for i in range(1,len(rows)):
   with self.subTest(removed=i):self.assertEqual(validate.validate_log(rows[:i]+rows[i+1:])['verdict'],'fail')
 def test_light_only_normalization(self):
  x=self.data();x['origins'][0]['fact']['OriginalBlockValue'] |= 15<<10
  self.assertEqual(validate.validate(x)['verdict'],'pass')
  x['origins'][0]['fact']['OriginalBlockValue'] |= 1<<14
  self.assertEqual(validate.validate(x)['verdict'],'fail')
 def test_missing(self):self.assertEqual(validate.validate({})['verdict'],'fail')
if __name__=='__main__':unittest.main()
