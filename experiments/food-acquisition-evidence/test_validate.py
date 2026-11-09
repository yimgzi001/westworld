import copy,unittest
from validate import validate

def fixture():
 return dict(schema=1,sessionId='s',actorId='a',sourceId='drop1',value=88,initialFishValue=162,baselineCount=0,sourceCount=1,uniqueSourceThroughout=True,inventoryInjection=False,
 acquisition=dict(sourceId='drop1',value=88,inventoryBefore=0,inventoryAfter=1,worldBefore=1,worldAfter=0,method='native_automatic_collection',time=2),
 eat=dict(requestId='r',sequence=3,value=88,eventValue=88,inventoryBefore=1,inventoryAfter=0,foodBefore=.5,foodAfter=.7,statsBefore=0,statsAfter=1,nativeStatus='succeeded',time=4),
 shutdown=dict(stopTime=5,drainedTime=5.2,budgetSeconds=2,idle=True,lateDispatches=0),dispatches=[dict(requestId='r',sequence=3,tool='act_Eat',time=3)])
class Tests(unittest.TestCase):
 def test_complete_synthetic(self):self.assertEqual(validate(fixture())['verdict'],'pass')
 def test_partial(self):
  e=fixture();del e['shutdown'];self.assertEqual(validate(e)['verdict'],'partial')
 def test_rejections(self):
  for path,value in [('baselineCount',1),('sourceCount',2),('uniqueSourceThroughout',False),('inventoryInjection',True),('initialFishValue',88),('acquisition.worldAfter',1),('acquisition.inventoryAfter',0),('acquisition.sourceId','other'),('eat.eventValue',162),('eat.inventoryAfter',1),('eat.foodAfter',.4),('eat.statsAfter',0),('eat.nativeStatus','accepted'),('eat.requestId','wrong'),('eat.time',1),('shutdown.idle',False),('shutdown.lateDispatches',1),('shutdown.drainedTime',9),('shutdown.stopTime',2),('eat.foodAfter',float('nan'))]:
   with self.subTest(path=path):
    e=fixture();o=e;parts=path.split('.')
    for k in parts[:-1]:o=o[k]
    o[parts[-1]]=value;self.assertEqual(validate(e)['verdict'],'fail')
 def test_duplicate(self):
  e=fixture();e['dispatches']*=2;self.assertEqual(validate(e)['verdict'],'fail')
 def test_missing_event(self):
  e=fixture();del e['eat']['eventValue'];self.assertEqual(validate(e)['verdict'],'partial')
 def test_null_partial(self):
  e=fixture();e['eat']=None;self.assertEqual(validate(e)['verdict'],'partial')
 def test_explicit_pickup_needs_receipt(self):
  e=fixture();e['acquisition']['method']='model_pickup_native_receipt';self.assertEqual(validate(e)['verdict'],'partial')
 def test_explicit_pickup_bound(self):
  e=fixture();e['acquisition']['method']='model_pickup_native_receipt';e['acquisition']['pickupReceipt']=dict(requestId='p',sequence=2,nativeStatus='succeeded');e['dispatches'].insert(0,dict(requestId='p',sequence=2,tool='act_PickUp',time=1.5));self.assertEqual(validate(e)['verdict'],'pass');e['dispatches'].pop(0);self.assertEqual(validate(e)['verdict'],'fail')
 def test_misspelled_pickup_rejected(self):
  e=fixture();e['acquisition']['method']='model_pickup_native_receipt';e['acquisition']['pickupReceipt']=dict(requestId='p',sequence=2,nativeStatus='succeeded');e['dispatches'].insert(0,dict(requestId='p',sequence=2,tool='act_Pickup',time=1.5))
  result=validate(e);self.assertEqual(result['verdict'],'fail');self.assertIn('pickup_request_not_uniquely_bound',result['failures'])
 def test_bad_shape(self):
  for e in (None,[],{'schema':1,'dispatches':[None]}):self.assertEqual(validate(e)['verdict'],'fail')
 def test_automatic_not_model(self):self.assertIn('not a model',validate(fixture())['pickupClaim'])
if __name__=='__main__':unittest.main()
