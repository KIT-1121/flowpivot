"""Generate the Windows FlowPivot icon from the sidebar's route mark (Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

S = 16
image = Image.new('RGBA', (32*S, 32*S))
draw = ImageDraw.Draw(image)
box = tuple(x*S for x in (1, 1, 31, 31))
draw.rounded_rectangle(box, radius=7*S, fill='#213735', outline='#527a72', width=S)
for path in (((7,10),(13,10),(20,16),(13,22),(7,22)), ((7,16),(25,16)), ((21,12),(25,16),(21,20))):
    points = [(x*S,y*S) for x,y in path]
    draw.line(points, fill='#86e0c6', width=2*S, joint='curve')
    for x,y in points:
        draw.ellipse((x-S,y-S,x+S,y+S), fill='#86e0c6')
image.resize((256,256),Image.Resampling.LANCZOS).save(
    Path(__file__).with_name('FlowPivot.ico'),sizes=[(n,n) for n in (16,24,32,48,64,128,256)],bitmap_format='bmp'
)
