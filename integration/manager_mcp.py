"""Local stdio MCP control surface. No model calls and no network listener."""
from __future__ import annotations
import ctypes
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
DATA = Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'CodexQuotaManager'
SETTINGS = DATA / 'settings.json'
STATE = DATA / 'state' / 'state.json'
DEFAULTS = dict(quotaBasis='layered', pollSeconds=30, pauseRemainingPercent=10, resumeRemainingPercent=20,
                weeklyPauseRemainingPercent=10, autoResumeEnabled=True, observeOnly=False, resumeAll=False,
                resumeThreadIds=[], resumeExcludedThreadIds=[], autoResetEnabled=False,
                resetWeeklyRemainingPercent=1, nearLimitRemainingPercent=20,
                nearLimitPollSeconds=10, maxConsecutiveFailures=0, showAllProjects=False,
                stateDirectory=str(DATA / 'state'))

def read(path, fallback=None):
    for attempt in range(4):
        try:
            return json.loads(path.read_text(encoding='utf-8-sig'))
        except FileNotFoundError:
            return fallback
        except (PermissionError, json.JSONDecodeError):
            if attempt == 3: raise
            time.sleep(.05)

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    os.replace(temporary, path)

def alive(pid):
    if not isinstance(pid, int) or pid <= 0: return False
    if os.name != 'nt':
        try: os.kill(pid, 0); return True
        except OSError: return False
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle: return False
    try:
        code = ctypes.c_ulong()
        return bool(kernel.GetExitCodeProcess(ctypes.c_void_p(handle), ctypes.byref(code))) and code.value == 259
    finally: kernel.CloseHandle(ctypes.c_void_p(handle))

def status():
    s = read(STATE, {})
    cfg = read(SETTINGS, DEFAULTS)
    age = None
    try: age = (datetime.now(timezone.utc) - datetime.fromisoformat(s['lastCheckedAt'])).total_seconds()
    except (KeyError, ValueError): pass
    quota = s.get('lastQuota') or {}
    return dict(running=alive(s.get('monitorProcessId')), fresh=age is not None and age < 120,
                age_seconds=round(age) if age is not None else None,
                automatic_control=not cfg.get('observeOnly', False),
                checked_at=s.get('lastCheckedAt'), phase=s.get('phase'),
                five_hour_remaining=quota.get('remaining'), weekly_remaining=quota.get('weeklyRemaining'),
                quota_basis=cfg.get('quotaBasis','layered'),
                weekly_floor_remaining=cfg.get('weeklyPauseRemainingPercent',10),
                pause_remaining=cfg.get('pauseRemainingPercent'), resume_remaining=cfg.get('resumeRemainingPercent'),
                auto_resume_enabled=cfg.get('autoResumeEnabled',True),
                managed_count=len(cfg.get('resumeThreadIds') or []), counts=s.get('counts'),
                purchases_enabled=False, reset_cards_enabled=bool(cfg.get('autoResetEnabled',False)),
                limitation='本机提前暂停不保证购买积分零消耗。')

def tasks(query=''):
    cfg = read(SETTINGS, DEFAULTS)
    managed = set(cfg.get('resumeThreadIds') or [])
    rows = []
    for chat in read(STATE, {}).get('chats', []):
        if query.casefold() not in (chat.get('title') or '').casefold(): continue
        rows.append(dict(thread_id=chat['id'], title=chat.get('title'), workspace=chat.get('workspace'), managed=chat['id'] in managed,
                         runtime=chat.get('runtime'), goal_status=chat.get('goalStatus'),
                         phase=chat.get('monitorPhase'), reason=chat.get('waitReason')))
    return sorted(rows, key=lambda x: not x['managed'])

def ensure_runner():
    if status()['running']: return
    flags = getattr(subprocess, 'CREATE_NO_WINDOW', 0) | getattr(subprocess, 'CREATE_NEW_PROCESS_GROUP', 0)
    subprocess.Popen([str(ROOT / 'dist' / 'CodexQuotaMonitor.exe'), '--tray', '--start-monitor'],
                     creationflags=flags, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

def configure(change):
    cfg = {**DEFAULTS, **read(SETTINGS, {})}
    cfg.update(change)
    cfg['resumeAll'] = False
    write(SETTINGS, cfg)
    return cfg

def manage(thread_id, action):
    uuid.UUID(thread_id)
    matches = [x for x in tasks() if x['thread_id'] == thread_id]
    if len(matches) != 1: raise ValueError('原聊天未在当前目录中唯一确认，请先查询任务列表。')
    cfg = read(SETTINGS, DEFAULTS)
    selected = set(cfg.get('resumeThreadIds') or [])
    excluded = set(cfg.get('resumeExcludedThreadIds') or [])
    if action == 'enable': selected.add(thread_id); excluded.discard(thread_id)
    elif action == 'disable': selected.discard(thread_id); excluded.add(thread_id)
    elif action not in ('pause', 'resume'): raise ValueError('Unknown action')
    configure(dict(resumeThreadIds=sorted(selected), resumeExcludedThreadIds=sorted(excluded)))
    actions = dict(enable='AutoContinue', disable='CancelAuto', pause='Pause', resume='Start')
    request_id = str(uuid.uuid4())
    write(DATA / 'state' / 'commands' / (str(time.time_ns()) + '-' + request_id + '.json'),
          dict(id=request_id, threadId=thread_id, title=matches[0]['title'], action=actions[action],
               time=datetime.now(timezone.utc).isoformat()))
    ensure_runner()
    return dict(queued=True, request_id=request_id, thread_id=thread_id, action=action,
                note='操作已登记，不代表执行已确认。查询该任务的状态及等待原因。未加载的原聊天在条件满足后自动打开；以实际状态为准。')

TOOLS = [
    dict(name='quota_status', description='查询本机额度托管器实际存活、额度新鲜度和配置；不调用模型。', inputSchema=dict(type='object', properties={})),
    dict(name='quota_tasks', description='查询原聊天编号、托管状态和等待原因；修改前用此工具定位目标。', inputSchema=dict(type='object', properties=dict(query=dict(type='string')))),
    dict(name='quota_manage', description='只操作用户指定的原聊天。enable登记托管并允许恢复，disable取消托管，pause保持人工暂停，resume请求继续一次。返回已排队不代表实际成功。', inputSchema=dict(type='object', properties=dict(thread_id=dict(type='string'), action=dict(type='string', enum=['enable','disable','pause','resume'])), required=['thread_id','action'])),
    dict(name='quota_global', description='启用或暂停全部自动控制；暂停全局控制不会中断当前任务。', inputSchema=dict(type='object', properties=dict(enabled=dict(type='boolean')), required=['enabled']))
]

def invoke(name, args):
    if name == 'quota_status': return status()
    if name == 'quota_tasks': return tasks(args.get('query',''))
    if name == 'quota_manage': return manage(args['thread_id'], args['action'])
    if name == 'quota_global':
        configure(dict(observeOnly=not args['enabled']))
        ensure_runner()
        return dict(requested=True, automatic_control=args['enabled'], note='后台下一轮应用；不自动解除人工暂停。')
    raise ValueError('Unknown tool')

def main():
    if len(sys.argv) > 1:
        print(json.dumps(status() if sys.argv[1] == 'status' else tasks(' '.join(sys.argv[2:])), ensure_ascii=False, indent=2)); return
    for line in sys.stdin:
        try:
            request = json.loads(line)
            if 'id' not in request: continue
            method = request.get('method')
            if method == 'initialize':
                result = dict(protocolVersion=request.get('params',{}).get('protocolVersion','2024-11-05'), capabilities=dict(tools=dict(listChanged=False)), serverInfo=dict(name='codex-quota-manager',version='1.0.0'))
            elif method == 'ping': result = {}
            elif method == 'tools/list': result = dict(tools=TOOLS)
            elif method == 'tools/call':
                try:
                    params=request['params'];value=invoke(params['name'],params.get('arguments',{}))
                    result=dict(content=[dict(type='text',text=json.dumps(value,ensure_ascii=False))],isError=False)
                except Exception as error: result=dict(content=[dict(type='text',text=str(error))],isError=True)
            else:
                print(json.dumps(dict(jsonrpc='2.0',id=request['id'],error=dict(code=-32601,message='Method not found'))),flush=True);continue
            print(json.dumps(dict(jsonrpc='2.0',id=request['id'],result=result),ensure_ascii=False),flush=True)
        except Exception as error:
            print(str(error),file=sys.stderr,flush=True)

if __name__ == '__main__': main()
