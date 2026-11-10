import unittest
from monitor import window_summary
class WindowTests(unittest.TestCase):
 def test_valid(self):
  e=[{'phase':'steady_start','wallUtc':'2026-01-01T00:00:00Z','gameTime':5},{'phase':'steady_end','wallUtc':'2026-01-01T00:00:30Z','gameTime':20}]
  s=[{'wallEpoch':1767225600+i,'cpuSeconds':i*2,'rssKiB':1024} for i in range(31)]
  r=window_summary(e,s);self.assertTrue(r['available']);self.assertEqual(r['averageCpuCores'],2);self.assertEqual(r['gameToWallRatio'],.5)
 def test_missing(self):self.assertFalse(window_summary([],[])['available'])
 def test_bad_offsets(self):
  e=[{'phase':'steady_start','wallUtc':'2026-01-01T00:00:00Z','gameTime':5},{'phase':'steady_end','wallUtc':'2026-01-01T00:00:30Z','gameTime':20}]
  s=[{'wallEpoch':1767225600+i,'cpuSeconds':i,'rssKiB':1024} for i in [2,28]]
  self.assertFalse(window_summary(e,s)['available'])
if __name__=='__main__':unittest.main()
