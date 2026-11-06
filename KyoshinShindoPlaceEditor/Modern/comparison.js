"use strict";
(() => {
  const $ = id => document.getElementById(id), api = ImageComparison;
  const canvases = [$("known"), $("latest"), $("difference")];
  let points = [], frame = null, base = null, result = null, selection = null;
  let nextId = 1, activeId = 0, timeout = 0, scale = 1, center = [176, 200];
  let selectedCode = null, candidates = [], visibleStations = [], documentId = null;
  let externalFocus = null;
  const layers = { known: document.createElement("canvas"), difference: document.createElement("canvas") };
  for (const layer of Object.values(layers)) { layer.width = 352; layer.height = 400; }
  function notice(text, error = false) { $("notice").textContent = text; $("notice").classList.toggle("error", error); }
  function busy(value) { for (const id of ["source", "refresh", "load", "snapshot", "send-main"]) $(id).disabled = value; }
  function request(action, extra = {}) {
    if (activeId) return;
    const id = nextId++; activeId = id; busy(true);
    notice(action === "refresh" ? "正在获取最新监测图片…" : "正在同步…");
    timeout = setTimeout(() => { activeId = 0; busy(false); notice("操作超时。当前画面未更新，可重试。", true); }, action === "loadImage" ? 300000 : 30000);
    window.chrome.webview.postMessage({ id, action, source: $("source").value, ...extra });
  }
  function decode(source) { return new Promise((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = () => reject(new Error("无法解码监测图片。")); image.src = source; }); }
  window.chrome.webview.addEventListener("message", async event => {
    const data = event.data;
    if (data.type === "focusFromEditor") {
      // A newer editor snapshot supersedes any outstanding comparison refresh.
      clearTimeout(timeout); activeId = 0; busy(false); clearSelection();
      points = data.state.points || []; documentId = data.state.documentId;
      $("file").textContent = data.state.path || "未保存的站点数据";
      calculate();
      const point = points.find(p => p.code === data.code);
      if (!point) { notice("站点已不存在，请重新同步。", true); return; }
      externalFocus = { code: data.code, pixel: [data.x, data.y] };
      selectedCode = data.code; selection = externalFocus.pixel;
      center = [data.x + .5, data.y + .5];
      // The main editor supports every integer zoom from 1 to 10.
      scale = data.scale; $("zoom").value = String(scale);
      updateDetails(); draw();
      const kind = point.pixel ? "正式坐标" : point.draft ? "待核对草稿" : "估算中心";
      notice(`已定位 ${point.code} 的${kind}。` + (visibleStations.some(p => p.code === point.code)
        ? "保留当前监测图片，可刷新获取最新图片。"
        : "此点未纳入当前对比统计，仅显示定位框；草稿/估算不是正式标注，停用筛选或地下来源也可能排除此站。"));
      document.getElementById("known").scrollIntoView({ block: "nearest" });
      return;
    }
    if (data.id !== activeId) return;
    clearTimeout(timeout);
    try {
      if (data.error) throw new Error(data.error);
      if (data.navigated) { notice("已传到主窗口，仅定位视图，不修改点位。"); return; }
      let nextFrame = frame;
      if (data.frame) {
        if (data.frame.source !== $("source").value) throw new Error("图片来源已变化，请重新获取。");
        const image = await decode(data.frame.image);
        if (image.width !== 352 || image.height !== 400) throw new Error("图像尺寸必须为 352×400。");
        nextFrame = { image, stamp: data.frame.stamp, source: data.frame.source };
      }
      if (data.id !== activeId) return;
      points = data.state.points || []; frame = nextFrame;
      if (documentId !== data.state.documentId) clearSelection();
      documentId = data.state.documentId;
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
    visibleStations = comparison.stations;
    candidates = candidates.filter(code => visibleStations.some(p => p.code === code));
    const focused = externalFocus && points.find(p => p.code === selectedCode);
    if (focused) { selection = focused.pixel || focused.draft || externalFocus.pixel; externalFocus.pixel = selection; updateDetails(); }
    else if (!visibleStations.some(p => p.code === selectedCode)) clearSelection();
    else { selection = visibleStations.find(p => p.code === selectedCode).pixel; updateDetails(); }
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
    function entry(container, text, pixel, code = null) {
      const button = document.createElement("button"); button.textContent = text;
      button.onclick = () => { clearSelection(); if (code) chooseStation(code); center = [pixel[0] + .5, pixel[1] + .5]; selection = pixel; scale = 5; $("zoom").value = "5"; draw(); };
      $(container).appendChild(button);
    }
    if (!result) return;
    for (const p of result.missingStations) entry("missing-list", `${p.code} ${p.name}`, p.pixel, p.code);
    for (const p of result.additions) entry("added-list", `(${p.x}, ${p.y}) · ${p.count} 像素`, [p.x, p.y]);
  }
  function draw() {
    if (!selectedCode) $("point-info").textContent = `当前视图中心：${center[0].toFixed(2)}, ${center[1].toFixed(2)} · ${scale}×。点击左图点位查看信息；未选点时传递此视图。`;
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
  function updateDetails() {
    const point = (externalFocus ? points : visibleStations).find(p => p.code === selectedCode);
    $("point-title").textContent = point ? `${point.code} · ${point.name}` : "未选择点位";
    $("send-main").textContent = point ? "在主窗口查看此站" : "将当前视图传到主窗口";
    $("clear-selection").disabled = !point;
    if (point) {
      const location = Number.isFinite(point.lat) && Number.isFinite(point.lon) ? `${point.lat.toFixed(5)}, ${point.lon.toFixed(5)}` : "未设置";
      const pixel = point.pixel || point.draft || externalFocus?.pixel;
      const label = point.pixel ? "图像坐标" : point.draft ? "待核对草稿（仅定位）" : "估算中心（仅定位）";
      $("point-info").textContent = `${point.type} · ${point.region || "未指定地区"} · ${point.suspended ? "停用" : "启用"} · 经纬度：${location} · ${label}：${pixel.join(", ")}`;
    }
    $("point-candidates").replaceChildren();
    if (candidates.length > 1) for (const code of candidates) {
      const point = visibleStations.find(p => p.code === code);
      if (!point) continue;
      const button = document.createElement("button"); button.textContent = `${point.code} ${point.name}`;
      button.setAttribute("aria-pressed", String(code === selectedCode));
      button.onclick = () => { chooseStation(code); draw(); };
      $("point-candidates").appendChild(button);
    }
  }
  function clearSelection() { externalFocus = null; selectedCode = null; candidates = []; selection = null; updateDetails(); }
  function chooseStation(code) { externalFocus = null; selectedCode = code; selection = visibleStations.find(p => p.code === code)?.pixel || null; updateDetails(); }
  function imagePosition(canvas, event) {
    const r = canvas.getBoundingClientRect();
    return [center[0] + (event.clientX - r.left - r.width / 2) / scale, center[1] + (event.clientY - r.top - r.height / 2) / scale];
  }
  for (const canvas of canvases) {
    let drag = null;
    canvas.addEventListener("pointerdown", event => {
      if (event.button !== 0 && event.button !== 2) return;
      drag = { x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY, moved: false, button: event.button };
      canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener("pointermove", event => {
      if (drag && (drag.moved || Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) > 4)) {
        center[0] -= (event.clientX - drag.x) / scale; center[1] -= (event.clientY - drag.y) / scale;
        drag.x = event.clientX; drag.y = event.clientY; drag.moved = true; draw();
      }
      const r = canvas.getBoundingClientRect(), x = Math.floor(center[0] + (event.clientX - r.left - r.width / 2) / scale), y = Math.floor(center[1] + (event.clientY - r.top - r.height / 2) / scale);
      $("pixel").textContent = `原图坐标：${x}, ${y}`;
    });
    canvas.addEventListener("pointerup", event => {
      if (drag && !drag.moved && drag.button === 0 && canvas.id === "known") {
        const [x,y] = imagePosition(canvas, event);
        const hits = api.hitTest(visibleStations, x, y, scale);
        candidates = hits.map(p => p.code);
        if (hits.length) chooseStation(hits[0].code); else clearSelection();
        draw();
      }
      drag = null;
    });
    canvas.addEventListener("pointercancel", () => { drag = null; });
    canvas.addEventListener("lostpointercapture", () => { drag = null; });
    canvas.addEventListener("contextmenu", event => event.preventDefault());
    canvas.addEventListener("wheel", event => { event.preventDefault(); scale = Math.max(1, Math.min(10, scale + (event.deltaY < 0 ? 1 : -1))); $("zoom").value = String(scale); draw(); }, { passive: false });
  }
  $("refresh").onclick = () => request("refresh"); $("load").onclick = () => request("loadImage"); $("snapshot").onclick = () => request("snapshot");
  $("source").onchange = () => { frame = null; clearSelection(); $("stamp").textContent = "来源已切换，等待新图片"; calculate(); request("refresh"); };
  $("suspended").onchange = $("footprint").onchange = calculate;
  $("zoom").onchange = () => { scale = Number($("zoom").value); draw(); };
  $("reset").onclick = () => { center = [176, 200]; scale = 1; clearSelection(); $("zoom").value = "1"; draw(); };
  $("clear-selection").onclick = () => { clearSelection(); draw(); };
  $("send-main").onclick = () => request("navigate", { documentId, code: selectedCode, x: center[0], y: center[1], scale });
  new ResizeObserver(draw).observe(document.querySelector("main"));
  decode("base-map.gif").then(image => { base = image; draw(); }).catch(() => notice("底图不可用；仍可比较监测像素。", true));
  request("ready");
})();
