import argparse
import json
import pathlib
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

parser = argparse.ArgumentParser()
parser.add_argument('--port', type=int, default=18763)
parser.add_argument('--log', type=pathlib.Path, required=True)
parser.add_argument('--guard', action='store_true')
args = parser.parse_args()
lock = threading.Lock()

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_POST(self):
        if self.headers.get('Transfer-Encoding', '').lower() == 'chunked':
            chunks = []
            while True:
                size = int(self.rfile.readline().strip().split(b';', 1)[0], 16)
                if size == 0:
                    self.rfile.readline()
                    break
                chunks.append(self.rfile.read(size))
                self.rfile.read(2)
            raw_body = b''.join(chunks)
        else:
            raw_body = self.rfile.read(int(self.headers.get('Content-Length', '0')))
        body = json.loads(raw_body)
        raw = body['input'].split('Character context JSON:\n', 1)[1].split('\nAvailable action schemas JSON:', 1)[0]
        context = json.loads(raw)
        cycle = context['cognitionCycle']
        active = context.get('task', {}).get('status') in ['waiting', 'running', 'blocked']
        with lock, args.log.open('a', encoding='utf-8') as stream:
            stream.write(json.dumps({'id': context['id'], 'duringTask': active}) + '\n')
        if not cycle['goals']:
            decision = dict(reason='Exercise one generic sustained task, then reassess while it is running.',
                deliberation=dict(operation='create', goalId='g1', expectedRevision=0, purpose='Observe for a bounded period',
                    source='observation', why='This is a deterministic scheduler contract check.',
                    method=dict(strategy='Wait while checking new interaction', expectedProgress=dict(metric='interaction', windowSeconds=5))),
                taskRequest=dict(operation='submit', steps=[dict(tool='act_Wait', arguments=dict(duration=40))]), reviewAfter=1)
            if args.guard:
                decision['taskRequest']['steps'] = [
                    dict(tool='act_MoveTo', arguments=dict(bearing=0, distance=1, range=.15), interrupt_on='body_worsened', next=3,
                        fallback=dict(failure='movement_interrupted', step=1)),
                    dict(tool='act_MoveTo', arguments=dict(bearing=0, distance=2, range=.15)),
                    dict(tool='act_Wait', arguments=dict(duration=8))]
        elif args.guard:
            goal = next(g for g in cycle['goals'] if g['id'] == cycle.get('activeGoal'))
            deliberation = dict(operation='maintain', goalId=goal['id'], expectedRevision=goal['revision'],
                source='body', why='Keep the explicitly authorized bounded contingency.')
            if active:
                deliberation['taskDisposition'] = dict(operation='retain', taskId=context['task']['taskId'],
                    expectedRevision=cycle['authorizationRevision'])
            decision = dict(reason='Verify native guarded task persistence.', deliberation=deliberation,
                taskRequest=dict(operation='continue'), reviewAfter=120)
        elif cycle.get('activeGoal'):
            goal = next(g for g in cycle['goals'] if g['id'] == cycle.get('activeGoal'))
            disposition = dict(operation='revoke', taskId=context['task']['taskId'], expectedRevision=cycle['authorizationRevision'])
            decision = dict(reason='Reassess and revoke the ongoing task before its natural completion.',
                deliberation=dict(operation='suspend', goalId=goal['id'], expectedRevision=goal['revision'], source='observation',
                    why='Verify active-task reconsideration uses the same runtime as idle reconsideration.', taskDisposition=disposition),
                taskRequest=dict(operation='stop'), reviewAfter=120)
        else:
            decision = dict(reason='Keep the already suspended goal suspended.',
                deliberation=dict(operation='none', source='observation', why='No new task is needed.'),
                taskRequest=dict(operation='none'), reviewAfter=120)
        data = json.dumps(dict(model='local-fixed', status='completed', usage=dict(input_tokens=100, output_tokens=100),
            output=[dict(type='message', role='assistant', content=[dict(type='output_text', text=json.dumps(decision))])])).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()
