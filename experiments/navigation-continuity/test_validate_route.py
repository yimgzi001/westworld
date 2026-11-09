import copy,importlib.util,json,pathlib,unittest,base64,gzip,hashlib
ROOT=pathlib.Path(__file__).parent
spec=importlib.util.spec_from_file_location('v',ROOT/'validate_route.py');v=importlib.util.module_from_spec(spec);spec.loader.exec_module(v)
def fixture(n):
 if n!=17:return json.loads((ROOT/f'fixtures/stage{n}-route-facts.json').read_text())
 manifest=json.loads((ROOT/'fixtures/stage17-route-facts.manifest.json').read_text())
 packed=base64.b64decode((ROOT/'fixtures/stage17-route-facts.json.gz.b64').read_text().strip(),validate=True)
 if manifest['encoding']!='gzip+base64' or hashlib.sha256(packed).hexdigest()!=manifest['gzipSha256']:raise ValueError('compressed fixture integrity')
 raw=gzip.decompress(packed)
 if len(raw)!=manifest['rawBytes'] or hashlib.sha256(raw).hexdigest()!=manifest['rawSha256']:raise ValueError('raw fixture integrity')
 return json.loads(raw)
class StrictTests(unittest.TestCase):
 def test17_first_route_yes_chain_no(self):
  r=v.validate(fixture(17));checks={c['check']:c['status'] for c in r['checks']};self.assertEqual(r['verdict'],'fail');self.assertEqual(checks['full_authorized_3m_endpoint'],'pass');self.assertEqual(checks['waypoint_1_no_sampled_low_speed'],'pass');self.assertEqual(checks['second_actual_arrived'],'fail')
 def test16_fallback_rejected(self):
  r=v.validate(fixture(16));self.assertEqual(r['verdict'],'fail');self.assertTrue(any(c['status']=='fail' for c in r['checks'] if c['check'] in ('first_arrived_single_segment','one_complete_route')))
 def test_ambiguous_receipt_unknown(self):
  rows=fixture(17);i=next(i for i,r in enumerate(rows) if r['kind']=='native_call_returned');rows.insert(i,copy.deepcopy(rows[i]));self.assertEqual(v.validate(rows)['verdict'],'unknown')
 def test_missing_sample_time_fails(self):
  rows=fixture(17)
  for r in rows:
   if r['kind']=='navigation_sample' and r['state']=='Moving':r.pop('monotonicSeconds');break
  self.assertTrue(any(c['check']=='continuous_sample_times' and c['status']=='fail' for c in v.validate(rows)['checks']))
 def test_short_route_not_full(self):
  rows=fixture(17)
  for r in rows:
   if r['kind']=='observed_flat_route_started':r['waypoints'][-1]['X']+=1;break
  self.assertTrue(any(c['check']=='full_authorized_3m_endpoint' and c['status']=='fail' for c in v.validate(rows)['checks']))
 def test_synthetic_second_success_can_pass(self):
  rows=fixture(17);terms=[r for r in rows if r['kind']=='bounded_navigation_terminal'];terms[1]['snapshot'].update(Reason='arrived',Path=.95,Remaining=.05)
  for r in rows:
   if r['kind']=='result':r['passed']=True
  self.assertEqual(v.validate(rows)['verdict'],'pass')
