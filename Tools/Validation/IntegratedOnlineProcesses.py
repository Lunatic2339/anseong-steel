import subprocess, time, json, pathlib, os, sys, traceback
base=pathlib.Path(__file__).resolve().parents[2]/'AnseongSteel/RobotPlayValidation'
root=base/('OnlineProcesses-'+time.strftime('%Y%m%d-%H%M%S'));root.mkdir()
exe=base/'IntegratedPlayer/IntegratedPlay.exe'
procs={};ids={};steps=[]
def read(role):
 try:return json.loads((root/(role+'-state.json')).read_text())
 except (OSError,ValueError):return {}
def wait(test,detail,timeout=25):
 end=time.monotonic()+timeout
 while time.monotonic()<end:
  if test():
   steps.append('PASS: '+detail);print(steps[-1],flush=True)
   (root/('checkpoint-%02d.json'%len(steps))).write_text(json.dumps({r:read(r) for r in procs},indent=2))
   return
  time.sleep(.1)
 raise RuntimeError(detail+' timeout '+json.dumps({r:read(r) for r in procs}))
def launch(role):
 startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
 procs[role]=subprocess.Popen([str(exe),'-batchmode','-screen-width','800','-screen-height','600','-logFile',str(root/(role+'.log')),'--integrated-online-check',role,str(root)],startupinfo=startup,creationflags=subprocess.CREATE_NO_WINDOW)
 ids[role]=0
def cmd(role,verb):
 ids[role]+=1;(root/(role+'-command.txt')).write_text(str(ids[role])+'|'+verb)
 wait(lambda:read(role).get('command',0)>=ids[role],role+' acknowledges '+verb)
def pair():return read('host').get('ready') and read('client').get('canRun')
def same(predicate):return all(predicate(read(r)) for r in ('host','client'))
def robot(d,k):return d.get('robot',{}).get(k)
def frame(d,k):return d.get('frame',{}).get(k,0)
try:
 launch('host');wait(lambda:read('host').get('spawned'),'host starts as left authority')
 wait(lambda:read('host').get('frame',{}).get('run')==4 and robot(read('host'),'bossHealth')==160,'host alone waits without battle damage')
 launch('client');wait(pair,'right client joins and both tracked inputs ready')
 wait(lambda:abs(read('client').get('seatOffset',{}).get('x',0)-.96)<.001,'client uses right cockpit offset')
 old_epoch=read('host')['connectionEpoch'];cmd('host','dupstart');cmd('client','dupstart');wait(lambda:pair() and read('host')['connectionEpoch']==old_epoch,'duplicate transport start preserves roles and epoch')
 wait(lambda:robot(read('host'),'hits')==0 and frame(read('client'),'run') in (0,4),'paired pilots still require host start')
 launch('extra');wait(lambda:read('extra').get('connection') in ('Disconnected','Failed') and not read('extra').get('authority'),'third pilot rejected without offline authority')
 cmd('extra','quit')
 cmd('client','run');cmd('client','restart');cmd('client','crossarm');cmd('host','crossarm')
 wait(lambda:read('host').get('rejectedControls',0)>=4 and read('host').get('rejectedInputs',0)>=2,'server rejects client start/restart and opposite-arm RPCs')
 wait(lambda:same(lambda d:robot(d,'bossHealth')==160 and robot(d,'robotHealth')==100 and frame(d,'lesson')==0),'unauthorized requests cannot change health or learning')
 cmd('host','run');wait(lambda:same(lambda d:frame(d,'run')==1),'host starts synchronized battle once')
 wait(lambda:read('host').get('starts')==1,'duplicate start is idempotent')
 cmd('host','jab');wait(lambda:same(lambda d:robot(d,'hits')==1 and frame(d,'lesson')==1),'server left jab synchronizes hook lesson')
 time.sleep(.9);cmd('client','hook');wait(lambda:same(lambda d:robot(d,'hits')==2 and frame(d,'lesson')==2),'right pilot hook synchronizes defence lesson')
 cmd('host','bossattack');wait(lambda:same(lambda d:robot(d,'robotHealth')==88 and abs(frame(d,'rightArm')-.12)<.001),'authored impact synchronizes HP and persistent right-arm damage')
 wait(lambda:abs(frame(read('host'),'elapsed')-frame(read('client'),'elapsed'))<.6 and frame(read('client'),'elapsed')>1,'client displays server encounter elapsed time')
 cmd('client','fakefeedback');time.sleep(.5)
 wait(lambda:same(lambda d:robot(d,'robotHealth')==88 and abs(frame(d,'rightArm')-.12)<.001),'client cannot broadcast invented damage feedback')
 wait(lambda:frame(read('host'),'punch')>5.5,'previous authored punch animation finishes')
 cmd('client','guard');time.sleep(.7);cmd('host','bossattack')
 wait(lambda:same(lambda d:frame(d,'lesson')==3 and robot(d,'robotHealth')==88),'server guard success synchronizes cooperative-combo lesson')
 cmd('client','neutral');cmd('host','neutral');time.sleep(.8)
 cmd('host','jab');wait(lambda:same(lambda d:robot(d,'hits')==3),'first online combo jab')
 time.sleep(.85);cmd('client','jab');wait(lambda:same(lambda d:robot(d,'hits')==4 and robot(d,'combo')==2),'right online jab advances shared combo')
 time.sleep(.85);cmd('host','hook');wait(lambda:robot(read('host'),'hits')==5,'final online hook contacts')
 cmd('host','boost');wait(lambda:same(lambda d:robot(d,'bossHealth')==76 and frame(d,'lesson')==4),'post-contact online boost and complete lesson replicate')
 before=frame(read('host'),'epoch');cmd('client','restart');time.sleep(.5)
 wait(lambda:frame(read('host'),'epoch')==before and robot(read('host'),'bossHealth')==76,'client retry cannot reset host run')
 cmd('host','restart');wait(lambda:same(lambda d:frame(d,'epoch')==before+1 and frame(d,'lesson')==0 and robot(d,'hits')==0 and robot(d,'bossHealth')==160 and frame(d,'rightArm')==0),'host retry synchronizes clean new run and persistent damage reset')
 wait(lambda:read('host').get('restarts')==1,'duplicate restart is idempotent')
 cmd('client','stop');wait(lambda:not read('host').get('remote') and frame(read('host'),'run')==4,'right pilot departure cancels run and returns host to waiting')
 wait(lambda:not read('client').get('authority'),'disconnected client never becomes offline authority')
 cmd('client','connect');wait(pair,'right pilot re-enters same host with fresh input sequence')
 wait(lambda:same(lambda d:frame(d,'lesson')==0 and robot(d,'hits')==0 and robot(d,'bossHealth')==160),'rejoined client receives authoritative reset snapshot')
 cmd('host','run');wait(lambda:same(lambda d:frame(d,'run')==1),'host explicitly restarts battle after rejoin')
 cmd('host','stop');wait(lambda:read('client').get('connection')=='Disconnected' and not read('client').get('authority'),'host shutdown removes client authority')
 cmd('host','connect');wait(lambda:read('host').get('spawned'),'host can restart transport after shutdown')
 cmd('client','connect');wait(pair,'both processes reconnect after host restart')
 wait(lambda:same(lambda d:robot(d,'bossHealth')==160 and frame(d,'lesson')==0),'transport restart preserves clean authoritative run')
 cmd('host','run');wait(lambda:same(lambda d:frame(d,'run')==1),'host starts fresh run after full transport reconnect')
 cmd('client','move');time.sleep(.45)
 wait(lambda:same(lambda d:abs(d.get('robot',{}).get('body',{}).get('x',1))<.02),'one remote stick alone cannot move shared body')
 cmd('host','move');wait(lambda:same(lambda d:d.get('robot',{}).get('body',{}).get('x',0)>.4),'matching host and client sticks move the same body')
 cmd('client','neutral');time.sleep(.2);x=read('host')['robot']['body']['x'];time.sleep(.4)
 wait(lambda:abs(read('host').get('robot',{}).get('body',{}).get('x',-1)-x)<.04,'releasing either pilot stick stops cooperative movement')
 cmd('host','restart');wait(lambda:same(lambda d:robot(d,'hits')==0 and abs(d.get('robot',{}).get('body',{}).get('x',1))<.01),'host retry resets synchronized movement position')
 outcome='PASS'
except Exception as ex:
 outcome='FAIL';steps.append('FAIL: '+str(ex));print(steps[-1],flush=True)
finally:
 (root/'online-checks.txt').write_text('\n'.join(steps)+'\nRESULT: '+outcome+'\nHMD/TWO PHYSICAL PCS: NOT TESTED\n')
 (root/'final-states.json').write_text(json.dumps({r:read(r) for r in procs},indent=2))
 for role,proc in procs.items():
  if proc.poll() is None:
   ids[role]+=1;(root/(role+'-command.txt')).write_text(str(ids[role])+'|quit')
 for proc in procs.values():
  try:proc.wait(timeout=8)
  except subprocess.TimeoutExpired:proc.terminate()
 print('Evidence: '+str(root),flush=True)
sys.exit(0 if outcome=='PASS' else 1)
