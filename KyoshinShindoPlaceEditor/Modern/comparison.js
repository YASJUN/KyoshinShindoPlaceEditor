"use strict";
(() => {
  const $ = id => document.getElementById(id), api = ImageComparison;
  const canvases = [$("known"), $("latest"), $("difference")];
  let points = [], frame = null, base = null, result = null, selection = null;
  let nextId = 1, activeId = 0, timeout = 0, scale = 1, center = [176, 200];
  const layers = { known: document.createElement("canvas"), difference: document.createElement("canvas") };
  for (const layer of Object.values(layers)) { layer.width = 352; layer.height = 400; }
  function notice(text, error = false) { $("notice").textContent = text; $("notice").classList.toggle("error", error); }
  function busy(value) { for (const id of ["source", "refresh", "load", "snapshot"]) $(id).disabled = value; }
  function request(action) {
    if (activeId) return;
    const id = nextId++; activeId = id; busy(true);
    notice(action === "refresh" ? "正在获取最新监测图片…" : "正在同步…");
    timeout = setTimeout(() => { activeId = 0; busy(false); notice("操作超时。当前画面未更新，可重试。", true); }, action === "loadImage" ? 300000 : 30000);
    window.chrome.webview.postMessage({ id, action, source: $("source").value });
  }
  function decode(source) { return new Promise((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = () => reject(new Error("无法解码监测图片。")); image.src = source; }); }
  window.chrome.webview.addEventListener("message", async event => {
    const data = event.data;
    if (data.id !== activeId) return;
    clearTimeout(timeout);
    try {
      if (data.error) throw new Error(data.error);
      let nextFrame = frame;
      if (data.frame) {
        if (data.frame.source !== $("source").value) throw new Error("图片来源已变化，请重新获取。");
        const image = await decode(data.frame.image);
        if (image.width !== 352 || image.height !== 400) throw new Error("图像尺寸必须为 352×400。");
        nextFrame = { image, stamp: data.frame.stamp, source: data.frame.source };
      }
      if (data.id !== activeId) return;
      points = data.state.points || []; frame = nextFrame;
      $("file").textContent = data.state.path || "未保存的站点数据";
      $("stamp").textContent = frame ? formatStamp(frame.stamp) : "尚未获取图片";
      calculate();
      notice(frame ? "对比已更新。点击下方条目可同步定位三张图。" : "点位已同步，请获取最新图片或打开本地监测图层。");
    } catch (error) { notice(error.message + " 当前画面未更新。", true); }
    finally { if (data.id === activeId) { activeId = 0; busy(false); } }
  });
  function formatStamp(stamp) {
    return /^\d{14}$/.test(stamp) ? `${stamp.slice(0,4)}-${stamp.slice(4,6)}-${stamp.slice(6,8)} ${stamp.slice(8,10)}:${stamp.slice(10,12)}:${stamp.slice(12,14)} JST` : stamp;
  }
  function calculate() {
    result = null;
    for (const layer of Object.values(layers)) layer.getContext("2d").clearRect(0, 0, 352, 400);
    const options = { source: $("source").value, includeSuspended: $("suspended").checked, radius: Number($("footprint").value) };
    const capture = document.createElement("canvas"); capture.width = 352; capture.height = 400;
    const context = capture.getContext("2d");
    if (frame) context.drawImage(frame.image, 0, 0);
    const comparison = api.compare(points, api.observedMask(context.getImageData(0, 0, 352, 400).data), options);
    const known = layers.known.getContext("2d"), knownPixels = known.createImageData(352, 400);
    for (let i = 0; i < comparison.expected.length; i++) if (comparison.expected[i]) knownPixels.data.set([53, 108, 166, 255], i * 4);
    known.putImageData(knownPixels, 0, 0);
    $("stations").textContent = comparison.stations.length;
    for (const name of ["missing", "added", "matched"]) $(name).textContent = frame ? comparison.counts[name].toLocaleString() : "—";
    if (frame) {
      result = comparison;
      const diff = layers.difference.getContext("2d"), pixels = diff.createImageData(352, 400);
      const colors = [null, [237,69,103,255], [0,185,203,255], [120,139,153,200]];
      for (let i = 0; i < result.labels.length; i++) if (result.labels[i]) pixels.data.set(colors[result.labels[i]], i * 4);
      diff.putImageData(pixels, 0, 0);
    }
    renderIssues(); draw();
  }
  function renderIssues() {
    $("missing-list").replaceChildren(); $("added-list").replaceChildren();
    $("missing-count").textContent = result ? `${result.missingStations.length} 站` : "";
    $("added-count").textContent = result ? `${result.additions.length} 处（非站点数量）` : "";
    function entry(container, text, pixel) {
      const button = document.createElement("button"); button.textContent = text;
      button.onclick = () => { center = pixel; selection = pixel; scale = 5; $("zoom").value = "5"; draw(); };
      $(container).appendChild(button);
    }
    if (!result) return;
    for (const p of result.missingStations) entry("missing-list", `${p.code} ${p.name}`, p.pixel);
    for (const p of result.additions) entry("added-list", `(${p.x}, ${p.y}) · ${p.count} 像素`, [p.x, p.y]);
  }
  function draw() {
    for (const canvas of canvases) {
      const rect = canvas.getBoundingClientRect(), ratio = devicePixelRatio || 1;
      canvas.width = Math.round(rect.width * ratio); canvas.height = Math.round(rect.height * ratio);
      const c = canvas.getContext("2d"); c.setTransform(ratio, 0, 0, ratio, 0, 0);
      c.fillStyle = "#edf2f4"; c.fillRect(0, 0, rect.width, rect.height);
      c.translate(rect.width / 2 - center[0] * scale, rect.height / 2 - center[1] * scale); c.scale(scale, scale);
      c.imageSmoothingEnabled = false;
      if (base) { c.globalAlpha = .45; c.drawImage(base, 0, 0); c.globalAlpha = 1; }
      if (canvas.id === "latest" && frame) c.drawImage(frame.image, 0, 0);
      else if (layers[canvas.id]) c.drawImage(layers[canvas.id], 0, 0);
      if (selection) {
        c.strokeStyle = "#ae7100"; c.lineWidth = 1.5 / scale;
        c.strokeRect(selection[0] - 3, selection[1] - 3, 7, 7);
      }
    }
  }
  for (const canvas of canvases) {
    let drag = null;
    canvas.addEventListener("pointerdown", event => { drag = [event.clientX, event.clientY]; canvas.setPointerCapture(event.pointerId); });
    canvas.addEventListener("pointermove", event => {
      if (drag) { center[0] -= (event.clientX - drag[0]) / scale; center[1] -= (event.clientY - drag[1]) / scale; drag = [event.clientX, event.clientY]; draw(); }
      const r = canvas.getBoundingClientRect(), x = Math.floor(center[0] + (event.clientX - r.left - r.width / 2) / scale), y = Math.floor(center[1] + (event.clientY - r.top - r.height / 2) / scale);
      $("pixel").textContent = `原图坐标：${x}, ${y}`;
    });
    canvas.addEventListener("pointerup", () => { drag = null; }); canvas.addEventListener("pointercancel", () => { drag = null; });
    canvas.addEventListener("contextmenu", event => event.preventDefault());
    canvas.addEventListener("wheel", event => { event.preventDefault(); const levels = [1,2,3,5,10], i = levels.indexOf(scale); scale = levels[Math.max(0, Math.min(levels.length - 1, i + (event.deltaY < 0 ? 1 : -1)))]; $("zoom").value = String(scale); draw(); }, { passive: false });
  }
  $("refresh").onclick = () => request("refresh"); $("load").onclick = () => request("loadImage"); $("snapshot").onclick = () => request("snapshot");
  $("source").onchange = () => { frame = null; selection = null; $("stamp").textContent = "来源已切换，等待新图片"; calculate(); request("refresh"); };
  $("suspended").onchange = $("footprint").onchange = calculate;
  $("zoom").onchange = () => { scale = Number($("zoom").value); draw(); };
  $("reset").onclick = () => { center = [176, 200]; scale = 1; selection = null; $("zoom").value = "1"; draw(); };
  new ResizeObserver(draw).observe(document.querySelector("main"));
  decode("base-map.gif").then(image => { base = image; draw(); }).catch(() => notice("底图不可用；仍可比较监测像素。", true));
  request("ready");
})();
