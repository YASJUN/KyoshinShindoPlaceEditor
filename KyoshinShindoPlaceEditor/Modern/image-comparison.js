"use strict";
(() => {
  const WIDTH = 352, HEIGHT = 400;
  // Compare presence, not shaking intensity. Transparent/gray text and backgrounds are excluded.
  function observedMask(rgba) {
    if (rgba.length !== WIDTH * HEIGHT * 4) throw new Error("图像尺寸必须为 352×400。");
    const mask = new Uint8Array(WIDTH * HEIGHT);
    for (let i = 0; i < mask.length; i++) {
      const r = rgba[i * 4], g = rgba[i * 4 + 1], b = rgba[i * 4 + 2];
      mask[i] = rgba[i * 4 + 3] >= 128 && Math.max(r, g, b) - Math.min(r, g, b) > 20 ? 1 : 0;
    }
    return mask;
  }
  function eligible(points, source, includeSuspended = false) {
    return points.filter(p => p.pixel && p.pixel.length === 2 && p.pixel.every(Number.isInteger)
      && p.pixel[0] >= 0 && p.pixel[0] < WIDTH && p.pixel[1] >= 0 && p.pixel[1] < HEIGHT
      && (includeSuspended || !p.suspended) && (source === "surface" || p.type === "KiK-net"));
  }
  function compare(points, observed, { source = "surface", includeSuspended = false, radius = 1 } = {}) {
    if (observed.length !== WIDTH * HEIGHT || ![0, 1].includes(radius)) throw new Error("无效的像素比较参数。");
    const stations = eligible(points, source, includeSuspended), expected = new Uint8Array(observed.length);
    for (const point of stations) {
      const [x, y] = point.pixel;
      for (let dy = -radius; dy <= radius; dy++) for (let dx = -radius; dx <= radius; dx++) {
        if (x + dx >= 0 && x + dx < WIDTH && y + dy >= 0 && y + dy < HEIGHT)
          expected[(y + dy) * WIDTH + x + dx] = 1;
      }
    }
    const labels = new Uint8Array(observed.length), counts = { missing: 0, added: 0, matched: 0 };
    for (let i = 0; i < labels.length; i++) {
      if (expected[i] && observed[i]) { labels[i] = 3; counts.matched++; }
      else if (expected[i]) { labels[i] = 1; counts.missing++; }
      else if (observed[i]) { labels[i] = 2; counts.added++; }
    }
    const missingStations = stations.filter(p => !observed[p.pixel[1] * WIDTH + p.pixel[0]]);
    // Eight-connected added components provide navigation; a cluster is not necessarily one station.
    const visited = new Uint8Array(labels.length), additions = [];
    for (let i = 0; i < labels.length; i++) {
      if (labels[i] !== 2 || visited[i]) continue;
      let minX = WIDTH, maxX = 0, minY = HEIGHT, maxY = 0, count = 0;
      const stack = [i]; visited[i] = 1;
      while (stack.length) {
        const q = stack.pop(), x = q % WIDTH, y = Math.floor(q / WIDTH);
        minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y); count++;
        for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
          const nx = x + dx, ny = y + dy, n = ny * WIDTH + nx;
          if (nx >= 0 && nx < WIDTH && ny >= 0 && ny < HEIGHT && labels[n] === 2 && !visited[n]) { visited[n] = 1; stack.push(n); }
        }
      }
      additions.push({ x: Math.round((minX + maxX) / 2), y: Math.round((minY + maxY) / 2), count });
    }
    return { expected, labels, counts, stations, missingStations, additions };
  }
  const api = { WIDTH, HEIGHT, observedMask, eligible, compare };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else window.ImageComparison = api;
})();
