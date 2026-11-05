(() => {
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  const tick = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  const upper = document.getElementById("monitor"), lower = document.getElementById("geographic");
  const context = lower.getContext("2d"), originalArc = context.arc;
  let arcs = [];
  context.arc = function (...args) { arcs.push({ args, color: this.strokeStyle }); return originalArc.apply(this, args); };
  window.prepareImageClick = async scale => {
    document.getElementById("notice").hidden = true;
    window.editorTest.select("CLICKROOT");
    window.editorTest.setZoom(scale);
    await tick();
    assert(document.getElementById("zoom-value").textContent === scale + "×", "Requested upper-map zoom was clamped");
    assert(document.getElementById("zoom-in").disabled === (scale === 10), "10x zoom button limit is incorrect");
    arcs = [];
    return true;
  };
  window.imageClickPosition = (x, y) => {
    const bounds = upper.getBoundingClientRect();
    const screen = upper.getContext("2d").getTransform().transformPoint({ x: x + 0.5, y: y + 0.5 });
    const result = { x: bounds.x + screen.x / devicePixelRatio, y: bounds.y + screen.y / devicePixelRatio };
    result.inside = result.x > bounds.left && result.x < bounds.right && result.y > bounds.top && result.y < bounds.bottom;
    result.centerX = bounds.x + bounds.width / 2;
    result.centerY = bounds.y + bounds.height / 2;
    return result;
  };
  window.verifyImageClick = async (x, y, geoX, geoY, officialX, officialY, sync = true) => {
    for (let i = 0; i < 100; i++) {
      await tick();
      const row = document.querySelector('.station-row[data-code="CLICKROOT"]');
      if (window.editorTest.state.pendingCount === 0 && row?.lastElementChild.textContent === `${x}, ${y}`) break;
    }
    await tick();
    assert(window.editorTest.state.selected === "CLICKROOT", "Overlapping image click changed the selected station");
    const row = document.querySelector('.station-row[data-code="CLICKROOT"]');
    assert(row?.getAttribute("aria-selected") === "true", "Selected station row lost its selection");
    assert(row.lastElementChild.textContent === `${x}, ${y}`, "Image click used the wrong scaled/panned pixel");
    const status = document.getElementById("geo-preview");
    const expected = ImageCoordinates.imageToLatLon(x, y);
    assert(!status.hidden && status.textContent.includes("临时"), "Missing temporary geographic preview");
    if (expected) {
      assert(status.textContent.includes(expected.latitude.toFixed(5)) && status.textContent.includes(expected.longitude.toFixed(5)),
        "Displayed latitude/longitude do not match inverse conversion");
      const screen = context.getTransform().transformPoint({ x: geoX, y: geoY });
      if (sync) assert(Math.abs(screen.x - lower.width / 2) <= 10.5 * devicePixelRatio
        && Math.abs(screen.y - lower.height / 2) <= 10.5 * devicePixelRatio, "Lower map did not center on converted coordinates");
      const pixels = context.getImageData(0, 0, lower.width, lower.height).data;
      let found = false;
      for (let i = 0; i < pixels.length; i += 4)
        if (pixels[i] === 176 && pixels[i + 1] === 100 && pixels[i + 2] === 0) { found = true; break; }
      assert(found, "Temporary preview marker is absent from the lower canvas: " + JSON.stringify({
        x, y, width: lower.width, height: lower.height, arcs, transform: context.getTransform().toString()
      }));
    } else {
      assert(status.textContent.includes("无法"), "Unmappable click has no explanation");
      assert(!arcs.some(arc => arc.color === "#b06400"), "Unmappable click left a stale preview marker");
    }
    assert(arcs.some(arc => arc.color === "#dc245c" && arc.args[0] === officialX && arc.args[1] === officialY),
      "Official selected-station ring disappeared when preview moved");
    return true;
  };
  window.verifyPreviewCleared = async () => {
    await tick();
    assert(document.getElementById("geo-preview").hidden, "Stale temporary preview survived reset");
    return true;
  };
  window.verifyPreviewZoom = async () => {
    const text = document.getElementById("geo-preview").textContent;
    window.editorTest.setZoom(11);
    await tick();
    assert(upper.getContext("2d").getTransform().a / devicePixelRatio === 10, "Upper zoom exceeded 10x");
    assert(document.getElementById("geo-preview").textContent === text && !document.getElementById("geo-preview").hidden,
      "Zoom cleared the active preview");
    return true;
  };
  window.finishImageClicks = () => { context.arc = originalArc; };
  return true;
})()
