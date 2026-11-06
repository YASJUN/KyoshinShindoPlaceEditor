"use strict";
(() => {
  const $ = id => document.getElementById(id);
  const ROW_HEIGHT = 30;
  const CONFLICT_COLORS = ["#c33737", "#006ab3", "#1c753f", "#9a42ad", "#995000", "#007078", "#a62b62", "#626c12"];
  const MAX_ZOOM = 10;
  let points = [], byCode = new Map(), filtered = [], selected = null, blink = true;
  let nextId = 1, queue = Promise.resolve(), pendingCount = 0, stateVersion = 0;
  let undoDepth = 0;
  let updatingNied = false;
  let testing = false;
  let comparisonOpen = false;
  let spatialQuery = null, searchTarget = null, documentId = null;
  let spatialDistances = new Map();
  const pending = new Map();
  const images = {};
  const pointLayer = Object.assign(document.createElement("canvas"), { width: 352, height: 400 });
  const geoLayer = Object.assign(document.createElement("canvas"), { width: 1024, height: 1024 });
  const monitor = { canvas: $("monitor"), x: 0, y: 0, scale: 1, width: 0, height: 0 };
  const geographic = { canvas: $("geographic"), x: -344, y: -520, scale: 1, width: 0, height: 0 };
  let conflicts = [], rowFrame = 0, drawFrame = 0, imageWarning = "", estimatedPoint = null;
  let conflictColors = new Map();
  let geographicPreview = null;
  let previewRevision = 0;
  const imagePixel = point => point?.pixel || point?.draft;

  function notice(text, error = false) {
    if (!text) return;
    $("notice").hidden = false;
    $("notice").classList.toggle("error", error);
    $("notice").querySelector("span").textContent = text;
  }

  function request(action, extra = {}) {
    const id = nextId++;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject, action });
      window.chrome.webview.postMessage({ id, action, ...extra });
    });
  }

  function command(action, extra = {}) {
    if (updatingNied) return Promise.resolve(null);
    if (action === "updateNied") {
      updatingNied = true;
      updateCommandAvailability();
    }
    pendingCount++;
    $("busy").textContent = updatingNied ? "正在下载 NIED 最新数据…" : "正在处理…";
    const task = queue.then(() => request(action, extra));
    queue = task.catch(error => notice(error.message, true)).finally(() => {
      pendingCount--;
      if (action === "updateNied") {
        updatingNied = false;
        updateCommandAvailability();
      }
      $("busy").textContent = pendingCount ? "正在处理…" : "";
    });
    return task;
  }

  window.chrome.webview.addEventListener("message", event => {
    const data = event.data;
    if (data.id) {
      const job = pending.get(data.id);
      if (!job) return;
      pending.delete(data.id);
      if (data.error) { job.reject(new Error(data.error)); return; }
      testing = data.testing === true;
      if (job.action === "open" || job.action === "undo") { geographicPreview = null; previewRevision++; }
      if (data.state) applyState(data.state, data.dirty, job.action === "undo");
      notice(data.notice);
      job.resolve(data);
      return;
    }
    if (data.type === "state") {
      geographicPreview = null;
      previewRevision++;
      applyState(data.state, data.dirty);
    }
    if (data.type === "images") {
      loadImage("osm", data.osm);
      loadImage("baseMap", data.baseMap);
    }
    if (data.type === "monitorImage") {
      loadImage("monitor", data.image);
      imageWarning = data.stamp.slice(8, 10) + ":" + data.stamp.slice(10, 12) + ":" + data.stamp.slice(12, 14) + " JST";
      $("image-status").textContent = imageWarning;
    }
    if (data.type === "imageError") {
      imageWarning = data.error;
      $("image-status").textContent = imageWarning;
    }
    if (data.type === "notice") notice(data.text);
    if (data.type === "comparisonAvailability") {
      comparisonOpen = data.open === true;
      updateComparisonAvailability();
    }
    if (data.type === "comparisonNavigate") {
      applyState(data.state, data.dirty);
      geographicPreview = null; previewRevision++; updateGeographicPreview();
      if (data.searchMode) {
        setZoom(data.scale);
        monitor.x = monitor.width / 2 - data.x * monitor.scale;
        monitor.y = monitor.height / 2 - data.y * monitor.scale;
        searchAt({ space: "image", x: data.x, y: data.y }, data.searchMode);
        return;
      }
      if (data.code && byCode.has(data.code)) {
        if (!filtered.some(point => point.code === data.code)) {
          spatialQuery = null;
          $("search").value = ""; $("only-unmarked").checked = false; $("station-status").value = "all";
          filterRows();
        }
        select(data.code);
        setZoom(data.scale);
        // Explicit cross-window navigation works even with automatic linking disabled.
        locate();
      } else {
        setZoom(data.scale);
        monitor.x = monitor.width / 2 - data.x * monitor.scale;
        monitor.y = monitor.height / 2 - data.y * monitor.scale;
        scheduleDraw();
      }
      notice(data.code ? "已定位对比窗口选中的站点。" : "已同步对比窗口的图像视野，当前站点保持不变。");
    }
  });

  function loadImage(key, source) {
    if (!source) return;
    const image = new Image();
    image.onload = () => {
      images[key] = image;
      if (key === "baseMap" && !imageWarning) $("image-status").textContent = "352×400";
      scheduleDraw();
    };
    image.onerror = () => notice("无法解码地图资源：" + key, true);
    image.src = source;
  }

  function applyState(state, dirty = false, preferServer = false) {
    if (state.documentId && documentId !== state.documentId) {
      spatialQuery = null;
      documentId = state.documentId;
      $("map-context-menu").hidden = true;
    }
    if (state.points) {
      points = state.points;
      byCode = new Map(points.map(point => [point.code, point]));
    } else if (state.patch) {
      const index = points.findIndex(point => point.code === state.patch.code);
      if (index >= 0) { points[index] = state.patch; byCode.set(state.patch.code, state.patch); }
    }
    if (preferServer && byCode.has(state.selected)) selected = state.selected;
    if (!byCode.has(selected)) selected = byCode.has(state.selected) ? state.selected : null;
    const pixel = imagePixel(byCode.get(selected));
    if (geographicPreview && (preferServer || geographicPreview.code !== selected
      || pixel?.[0] !== geographicPreview.x || pixel?.[1] !== geographicPreview.y)) geographicPreview = null;
    stateVersion++;
    undoDepth = state.undoCount;
    rebuildLayers();
    filterRows(false);
    $("filename").textContent = state.path || "未保存的站点数据";
    $("filename").title = state.path || "";
    $("dirty").textContent = dirty ? "●" : "";
    $("undo").disabled = state.undoCount === 0;
    $("undo").title = state.undoLabel ? "撤销 " + state.undoLabel + " (Ctrl+Z)" : "撤销 (Ctrl+Z)";
    $("save").disabled = $("saveAs").disabled = points.length === 0;
    $("counts").textContent = points.length + " 个站点 · " + points.filter(point => point.pixel).length + " 已标注";
    if (state.draftCount) $("counts").textContent += " · " + state.draftCount + " 待核对";
    updateSelection();
    updateCommandAvailability();
    scheduleDraw();
  }

  function updateCommandAvailability() {
    for (const button of document.querySelectorAll("[data-action]"))
      button.disabled = updatingNied;
    for (const id of ["save", "saveAs", "updateNied"])
      $(id).disabled = updatingNied || points.length === 0;
    $("undo").disabled = updatingNied || undoDepth === 0;
    $("clear").disabled = updatingNied || !imagePixel(byCode.get(selected));
    $("suspend").disabled = updatingNied || !byCode.has(selected);
    $("fillDrafts").disabled = updatingNied || !points.some(point => !point.pixel && !point.draft);
    $("confirmDraft").disabled = updatingNied || !byCode.get(selected)?.draft;
    $("refresh").disabled = $("source").disabled = updatingNied;
    updateComparisonAvailability();
  }

  function updateComparisonAvailability() {
    const point = byCode.get(selected), pixel = imagePixel(point) || estimatedPoint;
    $("send-comparison").hidden = !comparisonOpen;
    $("send-comparison").disabled = !comparisonOpen || updatingNied || !point || !pixel;
    $("send-comparison").title = pixel ? "发送正式坐标、草稿或估算中心，仅用于定位" : "请先选择具有有效图像坐标或估算中心的站点";
  }
  $("send-comparison").onclick = () => {
    const pixel = imagePixel(byCode.get(selected)) || estimatedPoint;
    if (comparisonOpen && pixel) command("sendComparison", { code: selected, x: pixel[0], y: pixel[1], scale: monitor.scale }).catch(() => {});
  };

  function rebuildLayers() {
    const upper = pointLayer.getContext("2d"), lower = geoLayer.getContext("2d");
    upper.clearRect(0, 0, 352, 400);
    lower.clearRect(0, 0, 1024, 1024);
    for (const point of points) {
      const pixel = imagePixel(point);
      if (pixel) {
        upper.fillStyle = point.draft ? "#b06400" : point.suspended ? "#929292" : point.type === "KiK-net" ? "#e34c4c" : "#e9a126";
        upper.fillRect(pixel[0], pixel[1], 1, 1);
      }
      if (point.geo) {
        lower.fillStyle = point.suspended ? "#929292" : point.pixel ? "#318b6b" : "#4478cb";
        lower.fillRect(point.geo[0] - 1, point.geo[1] - 1, 3, 3);
      }
    }
  }

  function filterRows(reset = true) {
    const query = $("search").value.trim().toLocaleLowerCase();
    const status = $("station-status").value, onlyUnmarked = $("only-unmarked").checked;
    const source = onlyUnmarked || status !== "all" ? points.filter(point => (!onlyUnmarked || !point.pixel)
      && (status === "all" || (status === "suspended" ? point.suspended : !point.suspended))) : points;
    filtered = query ? source.filter(point => [point.code, point.name, point.region].some(value => (value || "").toLocaleLowerCase().includes(query))) : source;
    spatialDistances = new Map();
    if (spatialQuery) {
      const matches = StationSearch.search(points, spatialQuery);
      spatialDistances = new Map(matches.map(item => [item.point.code, item.distance]));
      filtered = filtered.filter(point => spatialDistances.has(point.code))
        .sort((a, b) => spatialDistances.get(a.code) - spatialDistances.get(b.code) || a.code.localeCompare(b.code));
      const unit = spatialQuery.space === "image" ? "原图像素" : "公里";
      const distance = matches[0]?.distance;
      const location = StationSearch.mapLocation(spatialQuery.x, spatialQuery.y);
      $("spatial-summary").textContent = (spatialQuery.space === "image"
        ? `图像 (${spatialQuery.x.toFixed(1)}, ${spatialQuery.y.toFixed(1)})`
        : `地图 ${location.lat.toFixed(5)}, ${location.lon.toFixed(5)}`) + " · "
        + (spatialQuery.mode === "nearest" ? `最近未标记（未停用）：${distance == null ? "无候选" : distance.toFixed(2) + " " + unit}` : "附近点位")
        + ` · ${filtered.length} 个结果`;
      $("spatial-unit").textContent = unit;
      $("spatial-radius-label").hidden = spatialQuery.mode !== "nearby";
    }
    $("spatial-filter").hidden = !spatialQuery;
    $("virtual-space").style.height = filtered.length * ROW_HEIGHT + "px";
    $("table-scroll").setAttribute("aria-rowcount", filtered.length);
    $("empty").hidden = filtered.length !== 0;
    $("empty").textContent = points.length ? "没有匹配的站点" : "尚未载入点位文件";
    if (reset) $("table-scroll").scrollTop = 0;
    renderRows();
  }

  function searchAt(target, mode) {
    spatialQuery = { ...target, mode, radius: target.space === "image" ? 10 : 30 };
    $("search").value = "";
    $("only-unmarked").checked = mode === "nearest";
    $("station-status").value = mode === "nearest" ? "active" : "all";
    $("spatial-radius").value = spatialQuery.radius;
    filterRows(); scheduleDraw();
    notice(mode === "nearest" ? "已筛选最近的未标记且未停用点位，等距候选全部保留。图像查找优先使用草稿，否则使用估算中心。"
      : "已按距离筛选附近点位，可调整半径或继续使用文字、未标注和停用筛选。图像位置按正式坐标、草稿、估算中心依次取值。");
  }
  $("clear-spatial").onclick = () => {
    spatialQuery = null;
    $("search").value = ""; $("only-unmarked").checked = false; $("station-status").value = "all";
    filterRows(); scheduleDraw();
  };
  $("spatial-radius").onchange = () => {
    if (!spatialQuery) return;
    const radius = Number($("spatial-radius").value);
    if (!Number.isFinite(radius) || radius <= 0 || radius > 20000) { $("spatial-radius").value = spatialQuery.radius; return; }
    spatialQuery.radius = radius; filterRows(); scheduleDraw();
  };

  function renderRows() {
    rowFrame = 0;
    const viewport = $("table-scroll");
    const first = Math.max(0, Math.floor((viewport.scrollTop - ROW_HEIGHT) / ROW_HEIGHT) - 5);
    const count = Math.ceil(viewport.clientHeight / ROW_HEIGHT) + 12;
    const fragment = document.createDocumentFragment();
    for (let index = first; index < Math.min(filtered.length, first + count); index++) {
      const point = filtered[index];
      const row = document.createElement("div");
      row.className = "row station-row" + (point.code === selected ? " selected" : "");
      row.dataset.code = point.code;
      row.setAttribute("role", "row");
      row.setAttribute("aria-rowindex", index + 1);
      row.setAttribute("aria-selected", point.code === selected);
      styleConflictRow(row);
      const coordinates = point.lat == null ? "未设置" : point.lat.toFixed(5) + ", " + point.lon.toFixed(5);
      [point.code, point.type, point.suspended ? "是" : "否", point.name, point.region, coordinates,
        point.pixel ? point.pixel.join(", ") : point.draft ? "待核对 " + point.draft.join(", ") : "未设置"].forEach((value, column) => {
        const cell = document.createElement("span");
        cell.textContent = value || "";
        cell.title = value || "";
        if (spatialDistances.has(point.code)) cell.title += " · 距查找中心 " + spatialDistances.get(point.code).toFixed(2)
          + (spatialQuery.space === "image" ? " 原图像素" : " 公里");
        cell.setAttribute("role", "gridcell");
        if (column === 0) cell.className = "code";
        if (column === 6 && !point.pixel) cell.className = point.draft ? "draft" : "missing";
        row.appendChild(cell);
      });
      fragment.appendChild(row);
    }
    $("visible-rows").style.transform = "translateY(" + first * ROW_HEIGHT + "px)";
    $("visible-rows").replaceChildren(fragment);
  }

  function select(code, scroll = true) {
    if (!byCode.has(code)) return;
    geographicPreview = null;
    previewRevision++;
    selected = code;
    blink = true;
    updateSelection();
    for (const row of $("visible-rows").children) {
      row.classList.toggle("selected", row.dataset.code === code);
      row.setAttribute("aria-selected", row.dataset.code === code);
    }
    if (scroll) {
      const index = filtered.findIndex(point => point.code === code);
      const viewport = $("table-scroll"), top = index * ROW_HEIGHT;
      if (index >= 0 && (top < viewport.scrollTop || top + 2 * ROW_HEIGHT > viewport.scrollTop + viewport.clientHeight)) {
        viewport.scrollTop = Math.max(0, top - viewport.clientHeight / 2);
        renderRows();
      }
    }
    if ($("sync").checked) locate();
    else scheduleDraw();
  }

  function updateSelection() {
    const point = byCode.get(selected);
    const estimate = point && !imagePixel(point) ? ImageCoordinates.latLonToImage(point.lat, point.lon) : null;
    estimatedPoint = ImageCoordinates.isInsideImage(estimate) ? [estimate.x, estimate.y] : null;
    updateComparisonAvailability();
    $("clear").disabled = updatingNied || !imagePixel(point);
    $("clear").textContent = point?.draft ? "清除当前草稿" : "清除图像坐标";
    $("suspend").disabled = updatingNied || !point;
    $("confirmDraft").disabled = updatingNied || !point?.draft;
    $("selection").textContent = point ? point.code + " " + point.name + " | 图像坐标：" + (point.pixel ? point.pixel.join(", ") : "未设置") : "未选择观测点";
    if (point?.draft) {
      const [cx, cy] = point.draftCenter, [x, y] = point.draft;
      $("selection").textContent += " | 待核对草稿：" + x + ", " + y
        + " | 中心：" + cx + ", " + cy + " | 偏移：" + (x - cx) + ", " + (y - cy) + "（尚未写入）";
      if (x - cx < -8 || x - cx > 7 || y - cy < -8 || y - cy > 7)
        $("selection").textContent += "（偏移超出 -8～7，确认时重设中心）";
    } else if (point && !point.pixel) {
      $("selection").textContent += estimatedPoint ? " | 估算中心：" + estimatedPoint.join(", ") + "（仅供定位）"
        : estimate ? " | 估算中心：" + estimate.x + ", " + estimate.y + "（超出底图范围）"
        : " | 无法估算中心（经纬度无效或不在对应区域）";
    }
    $("selection").title = $("selection").textContent;
    updateGeographicPreview();
    const pixel = imagePixel(point);
    conflicts = pixel ? points.filter(other => {
      const candidate = imagePixel(other);
      return other !== point && candidate && Math.abs(candidate[0] - pixel[0]) <= 1 && Math.abs(candidate[1] - pixel[1]) <= 1;
    }) : [];
    conflicts.sort((a, b) => a.code < b.code ? -1 : a.code > b.code ? 1 : 0);
    conflictColors = new Map(conflicts.map((other, index) => [other.code, {
      number: index + 1, color: CONFLICT_COLORS[index] || `hsl(${index * 137.508 % 360} 65% 28%)`
    }]));
    $("conflicts").textContent = pixel ? "3×3 安全圈：" + (conflicts.length ? conflicts.length + " 个冲突（仅提示）" : "无其他点位") : "";
    for (const other of conflicts) {
      const entry = conflictColors.get(other.code), label = document.createElement("span");
      label.className = "conflict-station";
      label.dataset.code = other.code;
      label.style.setProperty("--conflict-color", entry.color);
      label.textContent = "#" + entry.number + " " + other.code + " " + other.name + (other.draft ? "（待核对）" : "");
      $("conflicts").appendChild(label);
    }
    $("conflicts").classList.toggle("warning", conflicts.length > 0);
    $("conflicts").title = conflicts.map(other => other.code + " " + other.name).join("\n");
    for (const row of $("visible-rows").children) styleConflictRow(row);
  }

  function styleConflictRow(row) {
    const entry = conflictColors.get(row.dataset.code);
    row.classList.toggle("conflicting", !!entry);
    if (entry) row.style.setProperty("--conflict-color", entry.color);
    else row.style.removeProperty("--conflict-color");
    row.title = entry ? "3×3 安全圈点位 #" + entry.number + "：" + row.dataset.code : "";
  }

  function drawConflictPoints(context, view, upper) {
    for (let index = 0; index < conflicts.length; index++) {
      const other = conflicts[index];
      const pixel = upper ? imagePixel(other) : other.geo;
      if (!pixel) continue;
      const x = pixel[0] + (upper ? 0.5 : 0), y = pixel[1] + (upper ? 0.5 : 0);
      context.fillStyle = context.strokeStyle = conflictColors.get(other.code).color;
      context.fillRect(pixel[0] - (upper ? 0 : 1), pixel[1] - (upper ? 0 : 1), upper ? 1 : 3, upper ? 1 : 3);
      // Clear the selected pixel's square even at 10x, then stagger overlapping rings.
      const radius = (upper ? Math.max(4, view.scale / Math.SQRT2 + 2) : 10) + index * 4;
      context.lineWidth = 2 / view.scale;
      context.beginPath();
      context.arc(x, y, radius / view.scale, 0, Math.PI * 2);
      context.stroke();
    }
  }

  function locate() {
    const point = byCode.get(selected);
    if (!point) return;
    if (imagePixel(point) || estimatedPoint) center(monitor, imagePixel(point) || estimatedPoint);
    if (geographicPreview?.geo || point.geo) center(geographic, geographicPreview?.geo || point.geo);
    scheduleDraw();
  }

  function updateGeographicPreview() {
    const status = $("geo-preview"), location = geographicPreview?.location;
    status.hidden = !geographicPreview;
    status.textContent = location ? "图像点击估算：" + location.latitude.toFixed(5) + ", "
      + location.longitude.toFixed(5) + "（临时）" : "无法换算此图像位置（临时预览）";
    status.title = status.textContent;
  }

  function locateStation() {
    geographicPreview = null;
    previewRevision++;
    updateGeographicPreview();
    locate();
  }

  function previewImageClick(code, x, y) {
    const point = byCode.get(selected);
    const pixel = imagePixel(point);
    if (selected !== code || pixel?.[0] !== x || pixel[1] !== y) return;
    const location = ImageCoordinates.imageToLatLon(x, y);
    let geo = null;
    if (location) {
      // Match MercatorProjection's PointF/truncation, zoom 5 and map origin (26,10) tiles.
      const sin = Math.max(-0.9999, Math.min(0.9999, Math.sin(location.latitude * Math.PI / 180)));
      geo = [
        Math.trunc(Math.fround(128 + location.longitude * (256 / 360)) * 32) - 26 * 256,
        Math.trunc(Math.fround(128 - Math.log((1 + sin) / (1 - sin)) * 256 / (4 * Math.PI)) * 32) - 10 * 256
      ];
    }
    geographicPreview = { code, x, y, location, geo };
    updateGeographicPreview();
    if ($("sync").checked && geo) center(geographic, geo);
    scheduleDraw();
  }
  function center(view, pixel) {
    view.x = view.width / 2 - (pixel[0] + 0.5) * view.scale;
    view.y = view.height / 2 - (pixel[1] + 0.5) * view.scale;
  }
  function scheduleDraw() {
    if (!drawFrame) drawFrame = requestAnimationFrame(() => { drawFrame = 0; draw(monitor, true); draw(geographic, false); });
  }
  function draw(view, upper) {
    const context = view.canvas.getContext("2d"), ratio = devicePixelRatio;
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.fillStyle = "#eff3f3";
    context.fillRect(0, 0, view.width, view.height);
    context.translate(view.x, view.y);
    context.scale(view.scale, view.scale);
    context.imageSmoothingEnabled = false;
    if (upper) {
      if ($("show-base").checked && images.baseMap) context.drawImage(images.baseMap, 0, 0);
      if (images.monitor) context.drawImage(images.monitor, 0, 0);
      if ($("show-points").checked) context.drawImage(pointLayer, 0, 0);
    } else {
      // The embedded map is 512px, displayed in the original 1024px Mercator extent.
      if (images.osm) context.drawImage(images.osm, 0, 0, 1024, 1024);
      context.drawImage(geoLayer, 0, 0);
    }
    if (!upper || $("show-points").checked) drawConflictPoints(context, view, upper);
    if (spatialQuery && spatialQuery.space === (upper ? "image" : "map")) {
      context.save();
      context.strokeStyle = "#7a36c4"; context.lineWidth = 2 / view.scale;
      const { x, y } = spatialQuery, arm = 8 / view.scale;
      context.beginPath(); context.moveTo(x - arm, y); context.lineTo(x + arm, y);
      context.moveTo(x, y - arm); context.lineTo(x, y + arm); context.stroke();
      if (upper && spatialQuery.mode === "nearby") {
        context.setLineDash([4 / view.scale, 3 / view.scale]);
        context.beginPath(); context.arc(x, y, spatialQuery.radius, 0, Math.PI * 2); context.stroke();
      }
      context.restore();
    }
    const point = byCode.get(selected), pixel = upper ? imagePixel(point) || estimatedPoint : point?.geo;
    if (!upper && geographicPreview?.geo) {
      const [x, y] = geographicPreview.geo;
      context.save();
      context.strokeStyle = "#b06400";
      context.lineWidth = 2 / view.scale;
      context.setLineDash([4 / view.scale, 3 / view.scale]);
      context.beginPath();
      context.arc(x, y, 10 / view.scale, 0, Math.PI * 2);
      context.stroke();
      context.setLineDash([]);
      context.beginPath();
      context.moveTo(x - 5 / view.scale, y); context.lineTo(x + 5 / view.scale, y);
      context.moveTo(x, y - 5 / view.scale); context.lineTo(x, y + 5 / view.scale);
      context.stroke();
      context.restore();
    }
    if (!pixel) return;
    context.lineWidth = 1 / view.scale;
    if (upper && !imagePixel(point)) {
      context.save();
      const x = pixel[0] + 0.5, y = pixel[1] + 0.5;
      context.lineWidth = 1.5 / view.scale;
      context.fillStyle = "#1676aa22";
      context.strokeStyle = "#1676aa";
      context.beginPath();
      context.arc(x, y, (blink ? 12 : 9) / view.scale, 0, Math.PI * 2);
      context.fill();
      context.stroke();
      context.fillStyle = "#1676aa";
      context.beginPath();
      context.arc(x, y, 2.5 / view.scale, 0, Math.PI * 2);
      context.fill();
      context.restore();
      return;
    }
    if (upper) {
      context.strokeStyle = conflicts.length ? "#ef3434" : "#00c6d1";
      context.strokeRect(pixel[0] - 1 + 0.5 / view.scale, pixel[1] - 1 + 0.5 / view.scale, 3 - 1 / view.scale, 3 - 1 / view.scale);
      if (blink) { context.fillStyle = "#ffff00"; context.fillRect(pixel[0], pixel[1], 1, 1); }
    } else {
      context.strokeStyle = "#dc245c";
      context.lineWidth = 2 / view.scale;
      context.beginPath();
      context.arc(pixel[0], pixel[1], 6 / view.scale, 0, Math.PI * 2);
      context.stroke();
    }
  }

  function setZoom(value) {
    const next = Math.max(1, Math.min(MAX_ZOOM, value));
    const x = (monitor.width / 2 - monitor.x) / monitor.scale, y = (monitor.height / 2 - monitor.y) / monitor.scale;
    monitor.scale = next;
    monitor.x = monitor.width / 2 - x * next; monitor.y = monitor.height / 2 - y * next;
    $("zoom-value").textContent = next + "×";
    $("zoom-out").disabled = next === 1; $("zoom-in").disabled = next === MAX_ZOOM;
    if ($("sync").checked) locate();
    scheduleDraw();
  }

  for (const view of [monitor, geographic]) {
    new ResizeObserver(() => {
      const previousWidth = view.width, previousHeight = view.height;
      const bounds = view.canvas.getBoundingClientRect();
      view.width = bounds.width; view.height = bounds.height;
      view.canvas.width = Math.round(bounds.width * devicePixelRatio);
      view.canvas.height = Math.round(bounds.height * devicePixelRatio);
      if (previousWidth) { view.x += (view.width - previousWidth) / 2; view.y += (view.height - previousHeight) / 2; }
      else if (view === monitor) { view.x = (view.width - 352) / 2; view.y = (view.height - 400) / 2; }
      if ($("sync").checked) locate();
      scheduleDraw();
    }).observe(view.canvas.parentElement);
    let drag = null;
    view.canvas.addEventListener("contextmenu", event => event.preventDefault());
    view.canvas.addEventListener("pointerdown", event => {
      event.preventDefault();
      $("map-context-menu").hidden = $("context-menu").hidden = true;
      view.canvas.focus();
      if (event.button === 2) {
        drag = { x: event.clientX, y: event.clientY, startX: view.x, startY: view.y, moved: false, pointerId: event.pointerId };
        view.canvas.setPointerCapture(event.pointerId);
        return;
      }
      const bounds = view.canvas.getBoundingClientRect();
      const x = (event.clientX - bounds.left - view.x) / view.scale, y = (event.clientY - bounds.top - view.y) / view.scale;
      if (view === monitor) {
        const px = Math.floor(x), py = Math.floor(y);
        if (event.button === 1) {
          const point = points.find(item => imagePixel(item)?.[0] === px && imagePixel(item)?.[1] === py);
          if (point) select(point.code);
        } else if (event.button === 0 && selected && px >= 0 && px < 352 && py >= 0 && py < 400) {
          const code = selected, revision = ++previewRevision;
          command("edit", { code, x: px, y: py }).then(result => {
            if (result && revision === previewRevision) previewImageClick(code, px, py);
          }).catch(() => {});
        }
      } else {
        let nearest = null, distance = (8 / view.scale) ** 2;
        for (const point of points) {
          if (!point.geo) continue;
          const squared = (point.geo[0] - x) ** 2 + (point.geo[1] - y) ** 2;
          if (squared <= distance) { nearest = point; distance = squared; }
        }
        if (nearest) select(nearest.code);
      }
    });
    view.canvas.addEventListener("pointermove", event => {
      if (!drag || event.pointerId !== drag.pointerId) return;
      if (Math.hypot(event.clientX - drag.x, event.clientY - drag.y) > 4) drag.moved = true;
      if (!drag.moved) return;
      view.x = drag.startX + event.clientX - drag.x; view.y = drag.startY + event.clientY - drag.y;
      scheduleDraw();
    });
    view.canvas.addEventListener("pointerup", event => {
      if (!drag || event.pointerId !== drag.pointerId) return;
      const moved = drag.moved || Math.hypot(event.clientX - drag.x, event.clientY - drag.y) > 4;
      if (!moved && event.button === 2) {
        const bounds = view.canvas.getBoundingClientRect();
        searchTarget = { space: view === monitor ? "image" : "map",
          x: (drag.x - bounds.left - view.x) / view.scale, y: (drag.y - bounds.top - view.y) / view.scale };
        const menu = $("map-context-menu"); menu.hidden = false;
        menu.style.left = Math.max(0, Math.min(innerWidth - menu.offsetWidth, event.clientX)) + "px";
        menu.style.top = Math.max(0, Math.min(innerHeight - menu.offsetHeight, event.clientY)) + "px";
      }
      drag = null;
      if (view.canvas.hasPointerCapture(event.pointerId)) view.canvas.releasePointerCapture(event.pointerId);
    });
    view.canvas.addEventListener("pointercancel", () => { drag = null; });
    view.canvas.addEventListener("lostpointercapture", () => { drag = null; });
    view.canvas.addEventListener("wheel", event => {
      event.preventDefault();
      if (view === monitor) setZoom(view.scale + (event.deltaY < 0 ? 1 : -1));
      else {
        const old = view.scale, next = Math.max(0.5, Math.min(MAX_ZOOM, old * (event.deltaY < 0 ? 1.25 : 0.8)));
        const bounds = view.canvas.getBoundingClientRect(), x = event.clientX - bounds.left, y = event.clientY - bounds.top;
        view.x = x - (x - view.x) * next / old; view.y = y - (y - view.y) * next / old; view.scale = next;
        scheduleDraw();
      }
    }, { passive: false });
  }

  $("table-scroll").addEventListener("scroll", () => { if (!rowFrame) rowFrame = requestAnimationFrame(renderRows); });
  $("visible-rows").addEventListener("click", event => { const row = event.target.closest(".station-row"); if (row) select(row.dataset.code, false); });
  $("visible-rows").addEventListener("contextmenu", event => {
    const row = event.target.closest(".station-row"); if (!row) return;
    event.preventDefault(); select(row.dataset.code, false);
    $("context-menu").hidden = false;
    $("context-menu").style.left = Math.min(innerWidth - 180, event.clientX) + "px";
    $("context-menu").style.top = Math.min(innerHeight - 85, event.clientY) + "px";
  });
  document.addEventListener("click", event => {
    const searchButton = event.target.closest("[data-station-search]");
    if (searchButton && searchTarget) searchAt(searchTarget, searchButton.dataset.stationSearch);
    const button = event.target.closest("[data-action]");
    if (button && !button.disabled) {
      const action = button.dataset.action;
      if ((action === "clear" || action === "suspend") && !selected) return;
      $("download-menu").open = false;
      command(action, { code: selected, network: button.dataset.network }).catch(() => {});
    }
    $("context-menu").hidden = true;
    $("map-context-menu").hidden = true;
  });
  document.addEventListener("keydown", event => {
    if (event.ctrlKey) {
      const key = event.key.toLowerCase(), action = key === "z" ? "undo" : key === "o" ? "open" : key === "s" ? (event.shiftKey ? "saveAs" : "save") : null;
      if (action) { event.preventDefault(); command(action).catch(() => {}); }
      if (key === "l") { event.preventDefault(); locateStation(); }
    } else if (document.activeElement === $("table-scroll") && ["ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) {
      event.preventDefault();
      const index = filtered.findIndex(point => point.code === selected);
      const next = event.key === "Home" ? 0 : event.key === "End" ? filtered.length - 1
        : Math.max(0, Math.min(filtered.length - 1, index + (event.key === "ArrowUp" ? -1 : 1)));
      if (filtered[next]) select(filtered[next].code);
    }
    if (event.key === "Escape") { $("context-menu").hidden = $("map-context-menu").hidden = true; $("notice").hidden = true; }
  });
  $("search").addEventListener("input", () => filterRows());
  $("only-unmarked").addEventListener("change", () => filterRows());
  $("station-status").addEventListener("change", () => filterRows());
  $("fillDrafts").onclick = () => {
    const candidates = [];
    for (const point of points) {
      if (point.pixel || point.draft) continue;
      const pixel = ImageCoordinates.latLonToImage(point.lat, point.lon);
      if (ImageCoordinates.isInsideImage(pixel)) candidates.push({ code: point.code, ...pixel });
    }
    command("fillDrafts", { candidates }).catch(() => {});
  };
  $("zoom-in").onclick = () => setZoom(monitor.scale + 1);
  $("zoom-out").onclick = () => setZoom(monitor.scale - 1);
  $("locate").onclick = locateStation;
  $("refresh").onclick = () => command("refresh", { source: $("source").value }).catch(() => {});
  $("source").onchange = () => {
    delete images.monitor; scheduleDraw();
    command("refresh", { source: $("source").value }).catch(() => {});
  };
  $("show-base").onchange = $("show-points").onchange = scheduleDraw;
  $("sync").onchange = () => { if ($("sync").checked) locate(); };
  $("dismiss").onclick = () => { $("notice").hidden = true; };
  new ResizeObserver(renderRows).observe($("table-scroll"));
  setInterval(() => { if (imagePixel(byCode.get(selected)) || estimatedPoint) { blink = !blink; scheduleDraw(); } }, 500);

  // Runtime tests exercise the same selection, zoom, virtualization and bridge used by the UI.
  window.editorTest = {
    get state() { return { count: points.length, selected, renderedRows: $("visible-rows").children.length, stateVersion, pendingCount,
      spatialQuery: spatialQuery && { ...spatialQuery }, filteredCodes: filtered.map(point => point.code) }; },
    select, setZoom, command,
    async benchmark(count = 100) {
      if (!testing) throw new Error("性能测试仅可在测试实例中运行。");
      let maximum = 0, previous = performance.now(), edits = 0, worst = null;
      const initialUndo = undoDepth;
      try {
        for (let index = 0; index < count; index++) {
          const start = performance.now();
          select(points[index * 47 % points.length].code);
          if (index % 10 === 0) setZoom(index / 10 % MAX_ZOOM + 1);
          const selectionEnd = performance.now();
          if (index % 3 === 0) {
            await command("edit", { code: selected, x: 100 + index % 30, y: 100 });
            edits++;
          }
          const commandEnd = performance.now();
          await new Promise(resolve => requestAnimationFrame(resolve));
          const now = performance.now();
          if (now - previous > maximum) {
            maximum = now - previous;
            worst = { index, selectMs: selectionEnd - start, bridgeMs: commandEnd - selectionEnd, frameMs: now - commandEnd };
          }
          previous = now;
        }
      } finally {
        while (undoDepth > initialUndo) await command("undo");
      }
      const context = $("monitor").getContext("2d");
      const pixels = context.getImageData(0, 0, $("monitor").width, $("monitor").height).data;
      let colors = new Set();
      for (let offset = 0; offset < pixels.length; offset += 400) colors.add(pixels.slice(offset, offset + 3).join(","));
      return { count: points.length, maximum, worst, edits, renderedRows: $("visible-rows").children.length, colors: colors.size, selected };
    }
  };
  command("ready").catch(error => notice("初始化失败：" + error.message, true));
})();
