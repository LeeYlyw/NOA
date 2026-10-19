import socket, subprocess, time, select
from pathlib import Path
exe=Path('Server/GameServer/x64/Debug/GameServer.exe').resolve()
p=subprocess.Popen([str(exe)],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=subprocess.CREATE_NO_WINDOW)
clients=[]
def receive(c, duration=.2):
    end=time.monotonic()+duration; result=b''
    while time.monotonic()<end:
        if select.select([c],[],[],max(0,end-time.monotonic()))[0]:
            chunk=c.recv(8192)
            if not chunk: break
            result+=chunk
    return result.decode()
def send(c,msg): c.sendall((msg+'\n').encode())
try:
    for n in range(200):
        try: a=socket.create_connection(('127.0.0.1',7777),.5); break
        except OSError: time.sleep(.1)
    else: raise RuntimeError("Local server did not become reachable on port 7777")
    clients.append(a)
    assert 'ID|1' in receive(a)
    send(a,'C_ROLE|2'); send(a,'C_READY|1')
    assert 'LOBBY|1|1|2|1' in receive(a)
    b=socket.create_connection(('127.0.0.1',7777),1); clients.append(b)
    assert 'ID|2' in receive(b)
    assert 'START|' not in receive(a)
    send(a,'C_START'); assert 'START|' not in receive(a)
    send(b,'C_ROLE|3'); send(b,'C_READY|1'); receive(a); receive(b)
    send(b,'C_START'); assert 'START|' not in receive(a)
    send(b,'C_ROLE|0'); assert 'LOBBY|1|1|2|1|1|0|0' in receive(a)
    send(a,'C_START'); assert 'START|' not in receive(a)
    send(b,'C_ROLE|3'); send(b,'C_READY|1'); receive(a); receive(b)
    send(a,'C_START'); one=receive(a); two=receive(b)
    start1=[x for x in one.splitlines() if x.startswith('START|')]
    start2=[x for x in two.splitlines() if x.startswith('START|')]
    assert start1==start2 and len(start1)==1 and start1[0].endswith('|2|3'),(one,two)
    print('PASS: solo lobby, no automatic start, minimum players, host authority, ready reset, matching seed and selected classes')
finally:
    for c in clients: c.close()
    try: p.wait(timeout=3)
    except subprocess.TimeoutExpired: p.terminate(); p.wait()

