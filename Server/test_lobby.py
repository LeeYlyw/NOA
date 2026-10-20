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
    send(a, 'C_PUZZLE_PAD|2|1')
    assert 'S_PUZZLE_PADS' not in receive(a)
    send(a, 'C_PUZZLE_PAD|1|1')
    assert 'S_PUZZLE_PADS|1|0' in receive(a)
    send(b, 'C_PUZZLE_PAD|2|1')
    clue_packets = receive(a).splitlines()
    choice_packets = receive(b).splitlines()
    clue = int(next(x.split('|')[1] for x in clue_packets if x.startswith('S_PUZZLE_CLUE|')))
    choices = [int(x) for x in next(x.split('|')[1:] for x in choice_packets if x.startswith('S_PUZZLE_CHOICES|'))]
    assert len(choices) == 4 and len(set(choices)) == 4 and clue in choices
    wrong = next(i for i, value in enumerate(choices) if value != clue)
    correct = choices.index(clue)
    send(a, f'C_PUZZLE_CHOOSE|1|{correct}')
    assert 'S_PUZZLE_SOLVED' not in receive(a)
    send(b, f'C_PUZZLE_CHOOSE|2|{wrong}')
    assert 'S_PUZZLE_WRONG' in receive(a)
    receive(b)
    send(b, 'C_PUZZLE_PAD|2|0')
    receive(a); receive(b)
    send(b, f'C_PUZZLE_CHOOSE|2|{correct}')
    assert 'S_PUZZLE_SOLVED' not in receive(a)
    send(b, 'C_PUZZLE_PAD|2|1')
    receive(a); receive(b)
    send(b, f'C_PUZZLE_CHOOSE|2|{correct}')
    result_a = receive(a)
    result_b = receive(b)
    assert 'S_PUZZLE_SOLVED' in result_a and 'S_CLUE_COUNT|1|3' in result_a
    assert 'S_PUZZLE_SOLVED' in result_b and 'S_CLUE_COUNT|1|3' in result_b
    print('PASS: solo lobby, no automatic start, minimum players, host authority, ready reset, matching seed and selected classes')
    print('PASS: separate clue/choices, both pads required, sender validation, wrong and correct answer synchronization')
finally:
    for c in clients: c.close()
    try: p.wait(timeout=3)
    except subprocess.TimeoutExpired: p.terminate(); p.wait()

