"""Extract the wooded skyline from Poly Haven's CC0 Shudu Lake 8K tonemapped JPG."""
from hashlib import sha256
from pathlib import Path
from sys import argv

import numpy as np
from PIL import Image

source, output = map(Path, argv[1:])
if sha256(source.read_bytes()).hexdigest() != "a4db1e5edf79687215e3ab847d419aec8e9ce250529dda8db8b64b52300d7e8a":
    raise ValueError("Expected the 8192 by 4096 Poly Haven Shudu Lake tonemapped JPG.")
with Image.open(source) as image:
    # This recorded 161-degree arc stays above the lake surface and boardwalk.
    band = np.asarray(image.convert("RGB").crop((0, 1480, 8192, 2000)))
    band = np.concatenate((band[:, -465:], band[:, :3191]), axis=1).copy()

# The overcast sky is several stops lighter than the trees. Find the first
# continuous dark run in each column, retaining the photographed branch tips.
intensity = band.astype(np.float32).mean(axis=2)
dark = intensity < 118
stable = dark[:-2] & dark[1:-1] & dark[2:]
edge = np.where(stable.any(axis=0), stable.argmax(axis=0), band.shape[0])
alpha = np.where(np.arange(band.shape[0])[:, None] >= edge, 255, 0).astype(np.uint8)
rgba = Image.fromarray(np.dstack((band, alpha)))
rgba.resize((2560, 364), Image.Resampling.LANCZOS).save(output, "WEBP", quality=89, method=6)
