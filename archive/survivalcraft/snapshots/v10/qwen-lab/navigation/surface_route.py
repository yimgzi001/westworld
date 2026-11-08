"""Read a surface snapshot and propose shortest <=8-step route to observed grass.
Snapshot-relative planning only. Does not refresh observation timestamps or send actions.
"""
import argparse,json,pathlib,math
from grid_planner import Cell,Planner
SAFE_TYPES={'SandBlock','GrassBlock','DirtBlock','StoneBlock','GravelBlock','ClayBlock'}
def plan_snapshot(data,position):
 cells={};types={}
 for row in data['surface']:
  if type(row.get('x'))!=int or type(row.get('z'))!=int or type(row.get('y'))!=int:continue
  if type(row.get('solid'))!=bool or type(row.get('headroom')) not in (float,int):continue
  key=(row['x'],row['z']);types[key]=row.get('type')
  cells[key]=Cell(key[0],key[1],row['y']+1,True,row['solid'],'solid' if row.get('type') in SAFE_TYPES else 'unknown',row['headroom'],0)
 start=(math.floor(position[0]),math.floor(position[2]));p=Planner(max_radius=8,max_steps=8)
 routes=[]
 for key,kind in types.items():
  if kind!='GrassBlock':continue
  r=p.plan(cells,start,key,0)
  if r.status=='ok':routes.append((len(r.cells),r.cells))
 if not routes:return {'snapshot_only':True,'status':'no_safe_grass_route_within_8_steps'}
 route=sorted(routes)[0][1]
 return {'snapshot_only':True,'status':'candidate','start_actual':position,'route':[{'cell':[x,z],'center':[x+.5,cells[x,z].y,z+.5],'type':types[x,z],'headroom':cells[x,z].headroom} for x,z in route]}
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('map');p.add_argument('--position',nargs=3,type=float,required=True);p.add_argument('--output',required=True);a=p.parse_args()
 src=pathlib.Path(a.map);out=plan_snapshot(json.loads(src.read_text()),a.position);out['source_file_mtime_unix']=src.stat().st_mtime
 pathlib.Path(a.output).write_text(json.dumps(out,indent=2));print(json.dumps(out))
