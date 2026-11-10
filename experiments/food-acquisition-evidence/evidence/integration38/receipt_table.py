#!/usr/bin/env python3
"""可读、无损的重复导航收据表；不是通用压缩，不执行日志内容。"""
import collections,json,re
NUMBER=r'-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?'
FIELD=re.compile(r'"(Sequence|segments|path|remaining|Immersion|VerticalVelocity|HorizontalSpeed|Health|elapsed_seconds|budget_seconds)":('+NUMBER+r')')
NUM=re.compile(NUMBER+r'\Z');MARK='_receipt_table'
def numeric_fields(line):
 seen=collections.Counter();fields=[]
 for m in FIELD.finditer(line):
  seen[m[1]]+=1;name=m[1] if seen[m[1]]==1 else m[1]+'['+str(seen[m[1]])+']'
  fields.append((name,m.start(2),m.end(2),m[2]))
 return fields

def encode_log(text):
 templates=[];lookup={};output=[];pending=[];pid=None;rows=0;status_rows=0
 def flush():
  if not pending:return
  defaults={f[0]:f[3] for f in numeric_fields(templates[pid])};columns=[k for k,v in defaults.items() if any(row[k]!=v for row in pending)]
  record={MARK:pid,'columns':columns,'rows':[[row[k] for k in columns] for row in pending]}
  output.append(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n');pending.clear()
 for line in text.splitlines(keepends=True):
  obj=json.loads(line);assert MARK not in obj,'原始事件使用了归档保留键';rows+=1
  if obj.get('kind')!='tool_receipt' or obj.get('receipt',{}).get('Tool')!='host_navigation_status':flush();pid=None;output.append(line);continue
  fields=numeric_fields(line);assert fields and fields[0][0]=='Sequence'
  key=FIELD.sub(lambda m:'"'+m[1]+'":#',line)
  if key not in lookup:lookup[key]=len(templates);templates.append(line)
  tid=lookup[key]
  if pid!=tid or len(pending)>=256:flush();pid=tid
  pending.append({name:value for name,_,_,value in fields});status_rows+=1
 flush()
 return ''.join(templates),''.join(output),{'originalRows':rows,'navigationStatusRows':status_rows,'templateCount':len(templates),'streamRecords':len(output),'encoding':'readable-named-numeric-table-v1','numericTextPreserved':True,'allOtherRowsVerbatim':True}

def decode_log(templates_text,stream_text):
 templates=templates_text.splitlines(keepends=True);cache=[numeric_fields(t) for t in templates];output=[]
 for line in stream_text.splitlines(keepends=True):
  record=json.loads(line)
  if MARK not in record:output.append(line);continue
  assert set(record)=={MARK,'columns','rows'};tid=record[MARK];assert type(tid) is int and 0<=tid<len(templates);template=templates[tid];fields=cache[tid];columns=record['columns'];assert isinstance(columns,list) and len(set(columns))==len(columns) and set(columns)<={f[0] for f in fields}
  assert isinstance(record['rows'],list) and 1<=len(record['rows'])<=256
  for row in record['rows']:
   assert isinstance(row,list) and len(row)==len(columns) and all(isinstance(v,str) and NUM.fullmatch(v) for v in row)
   updates=dict(zip(columns,row));pieces=[];start=0
   for name,a,b,value in fields:pieces.extend((template[start:a],updates.get(name,value)));start=b
   pieces.append(template[start:]);output.append(''.join(pieces))
 return ''.join(output)
