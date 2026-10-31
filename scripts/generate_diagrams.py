#!/usr/bin/env python3
"""Generate vector wiring illustrations (not external photos) with correct DIP pin numbers.

Top view is normalized; rotate PCB so the lever is LEFT (for illustration).
Socket 24: top row 1..12, bottom row 24..13 left to right.
DIP20 at lever end occupies socket positions 1..10 and 24..15.
Adapters are UNVERIFIED symbols, never presented as physical pinout instructions.
"""
from pathlib import Path
from html import escape

DEST = Path(__file__).resolve().parents[1] / 'AfterburnerStudio' / 'Assets' / 'Diagrams'
DEST.mkdir(parents=True, exist_ok=True)


def text(x, y, s, size=18, color='#17324b', weight='normal'):
    return f'<text x="{x}" y="{y}" font-family="Arial,DejaVu Sans,sans-serif" font-size="{size}" fill="{color}" font-weight="{weight}">{escape(str(s))}</text>'


def shell(title):
    return ['''<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="630" viewBox="0 0 1000 630">''',
            '<rect width="1000" height="630" fill="#f4f8fc"/>',
            '<rect x="20" y="20" width="960" height="590" rx="18" fill="white" stroke="#d5e0e9" stroke-width="3"/>',
            text(50, 65, title, 28, '#16334f', 'bold'),
            text(50, 94, 'Pohled shora; natocte skutecnou desku packou vlevo.', 16, '#53697d')]


def socket_base(s):
    s.extend(['<rect x="140" y="155" width="730" height="355" rx="25" fill="#0d8e99" stroke="#16485a" stroke-width="8"/>',
              '<rect x="182" y="190" width="642" height="280" rx="7" fill="#d5f3f2" stroke="#26536b" stroke-width="2"/>',
              '<line x1="145" y1="274" x2="110" y2="155" stroke="#253d51" stroke-width="15" stroke-linecap="round"/>',
              '<rect x="88" y="126" width="52" height="60" rx="21" fill="#ddebf0" stroke="#253d51" stroke-width="5"/>',
              text(45, 309, 'PACKA', 16, '#345167','bold')])
    x0=219; step=52
    for i in range(12):
        x=x0+i*step
        s.append(f'<rect x="{x-9}" y="214" width="18" height="43" fill="#d8ac4f" stroke="#35495a" stroke-width="1"/>')
        s.append(f'<rect x="{x-9}" y="395" width="18" height="43" fill="#d8ac4f" stroke="#35495a" stroke-width="1"/>')
        s.append(text(x-6, 205, i+1, 14, '#22384e', 'bold'))
        s.append(text(x-11, 461, 24-i, 14, '#22384e', 'bold'))
    return x0,step


def make_dip(pins):
    assert pins in (20,24)
    s=shell(f'GAL / ATF — DIP{pins}: bez adapteru')
    x0,step=socket_base(s)
    n=pins//2; x1=x0-28; w=(n-1)*step+56
    s.append(f'<rect x="{x1}" y="276" width="{w}" height="108" rx="11" fill="#24333b" stroke="#13212b" stroke-width="4"/>')
    s.append(f'<circle cx="{x0-6}" cy="293" r="7" fill="#ef4444"/>')
    s.append(text(x0+52,333,f'GAL / ATF DIP{pins}',24,'white','bold'))
    for i in range(n):
        x=x0+i*step
        s.append(f'<rect x="{x-11}" y="257" width="21" height="19" fill="#c1cbd0"/>')
        s.append(f'<rect x="{x-11}" y="384" width="21" height="11" fill="#c1cbd0"/>')
        s.append(text(x-9,302,i+1,12,'#e5f4f6'))
        s.append(text(x-12,366,pins-i,12,'#e5f4f6'))
    if pins==20:
        s.append('<rect x="741" y="270" width="95" height="110" rx="10" fill="#eaf2f5" stroke="#7c919f" stroke-dasharray="8 6"/>')
        s.append(text(749,321,'4 VOLNE',13,'#53697d','bold'))
        s.append(text(750,340,'KONTAKTY',11,'#53697d'))
    s.append(text(51,553,'PIN 1: cerveny bod u packy ZIF. DIP cisla jsou uvnitr cipu; cisla ZIF jsou mimo.',18,'#176d5a'))
    s.append(text(51,582,'Poloha je normalizovana; pred osazenim over skutecnou desku.',15,'#53697d'))
    s.append('</svg>')
    out=DEST/f'DIP{pins}_direct.svg';out.write_text('\n'.join(s),encoding='utf-8');return out


def make_adapter(name, label):
    s=shell(f'{name}: adapter je povinny')
    socket_base(s)
    s.extend(['<rect x="330" y="260" width="370" height="130" rx="10" fill="#a2d5ba" stroke="#16724f" stroke-width="5"/>',
              '<rect x="430" y="281" width="180" height="84" rx="10" fill="#23323d"/>',
              text(444,331,label,22,'white','bold'),
              text(382,379,'SYMBOLICKE UMISTENI',14,'#b91c1c','bold'),
              '<rect x="46" y="523" width="910" height="70" rx="10" fill="#fff0df" stroke="#e0a55b"/>',
              text(60,553,'NEOVERENO: Pin 1 a orientaci adapteru NELZE urcit z tohoto obrazku!',18,'#a23d23','bold'),
              text(60,576,'Pouzij schema a potisk spravne revize adapteru. Zadny skutecny pinout zde neni.',15,'#84522a'),
              '</svg>'])
    out=DEST/f'{name}_adapter_UNVERIFIED.svg';out.write_text('\n'.join(s),encoding='utf-8');return out


if __name__=='__main__':
    paths=[make_dip(20),make_dip(24),make_adapter('PEEL18CV8','PEEL18CV8'),
           make_adapter('GAL26CV12','PLCC28'),make_adapter('ATF1504AS','PLCC44 / JTAG')]
    print('\n'.join(map(str,paths)))
