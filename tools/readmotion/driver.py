"""Plays DandyPicker while readmotion.json records it (Windows only).

Puts the window at the spot the recording grabs, then glides the real mouse through the
app: the plants growing in, swaying, being customized, and the code they spell. Points are
in physical pixels from the window's top-left, for the size set in PLACE.

    python driver.py          play it (readmotion starts this once recording begins)
    python driver.py --place  just move the window there, for measuring
"""
import ctypes
import ctypes.wintypes as wt
import sys
import time

u = ctypes.windll.user32
u.SetProcessDPIAware()

TITLE = "DandyPicker"
PLACE = (40, 20, 1492, 1180)      # x, y, w, h of the window: readmotion.json's region matches
X0, Y0 = PLACE[0], PLACE[1]


def find(title, timeout=10.0):
    end = time.time() + timeout
    while time.time() < end:
        h = u.FindWindowW(None, title)
        if h:
            return h
        time.sleep(0.1)
    sys.exit(f"no window called {title}")


def place(h):
    u.ShowWindow(h, 9)                       # restore
    u.MoveWindow(h, *PLACE, True)
    u.SetForegroundWindow(h)


def pos():
    p = wt.POINT()
    u.GetCursorPos(ctypes.byref(p))
    return p.x, p.y


def glide(x, y, secs=0.6):
    """Eases the mouse to (x, y), relative to the window."""
    sx, sy = pos()
    tx, ty = X0 + x, Y0 + y
    n = max(1, int(secs * 60))
    for i in range(1, n + 1):
        t = i / n
        t = t * t * (3 - 2 * t)
        u.SetCursorPos(int(sx + (tx - sx) * t), int(sy + (ty - sy) * t))
        time.sleep(secs / n)


def click(x, y, secs=0.6, after=0.8):
    glide(x, y, secs)
    time.sleep(0.12)
    u.mouse_event(0x02, 0, 0, 0, 0)
    time.sleep(0.06)
    u.mouse_event(0x04, 0, 0, 0, 0)
    time.sleep(after)


def drag(x1, y1, x2, y2, secs=0.9, after=0.4):
    glide(x1, y1, 0.5)
    u.mouse_event(0x02, 0, 0, 0, 0)
    glide(x2, y2, secs)
    u.mouse_event(0x04, 0, 0, 0, 0)
    time.sleep(after)


def key(vk, after=0.5):
    u.keybd_event(vk, 0, 0, 0)
    time.sleep(0.03)
    u.keybd_event(vk, 0, 2, 0)
    time.sleep(after)


def type_text(text, per=0.09):
    for ch in text:
        r = u.VkKeyScanW(ord(ch))
        vk, shift = r & 0xFF, (r >> 8) & 1
        if shift:
            u.keybd_event(0x10, 0, 0, 0)
        key(vk, per)
        if shift:
            u.keybd_event(0x10, 0, 2, 0)


def wait(s):
    time.sleep(s)


BEAT = 5.0       # seconds per beat; readmotion.json cuts one clip per beat
LEAD = 0.0       # the plants start growing as soon as the window opens


def play():
    h = find(TITLE)
    place(h)
    u.SetCursorPos(X0 + 1000, Y0 + 640)
    t0 = time.time()

    def at(beat, offset=0.0):
        wait(max(0.0, t0 + LEAD + beat * BEAT + offset - time.time()))

    # 0. the plants grow in, one after another, as the window opens
    at(0, 0.5)
    glide(700, 1000, 2.0)
    # 1. hovering makes a plant sway
    at(1)
    glide(353, 420, 0.9); wait(0.6)
    glide(746, 420, 0.9); wait(0.6)
    glide(1139, 420, 0.9)
    # 2. click a plant to customize it: color, waviness, stem bend, seed tips
    at(2)
    click(353, 450, after=0.8)
    click(898, 446, after=0.5)               # pink
    drag(879, 547, 1180, 547)                # waviness
    drag(1028, 649, 820, 649, secs=0.7)      # stem bend
    click(961, 758, after=0.3)               # Star tips
    # 3. the seeds spell the profile's name and folder, like a QR code
    at(3)
    click(436, 962, after=0.2)               # Show the code
    at(3, 4.4)
    click(1034, 1042, secs=0.4, after=0.1)   # Cancel: the demo keeps its look
    at(3, 5.0)


if __name__ == "__main__":
    if "--place" in sys.argv:
        place(find(TITLE))
    else:
        play()
