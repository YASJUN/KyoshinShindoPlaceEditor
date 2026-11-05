(async () => {
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  const tick = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  assert(typeof window.ImageCoordinates === "object", "Independent image coordinate module is not loaded");
  const canvas = document.getElementById("monitor"), context = canvas.getContext("2d");
  const info = document.getElementById("selection");
  const safety = document.getElementById("conflicts");
  let safetyDraws = 0;
  const strokeRect = context.strokeRect;
  context.strokeRect = function (...args) { safetyDraws++; return strokeRect.apply(this, args); };
  const isBlue = (x, y) => {
    const p = context.getImageData(Math.floor(x), Math.floor(y), 1, 1).data;
    return p[0] === 22 && p[1] === 118 && p[2] === 170;
  };
  function hasBlue() {
    const p = context.getImageData(0, 0, canvas.width, canvas.height).data;
    for (let i = 0; i < p.length; i += 4)
      if (p[i] === 22 && p[i + 1] === 118 && p[i + 2] === 170) return true;
    return false;
  }
  try {
    document.getElementById("notice").hidden = true;
    document.getElementById("sync").checked = true;
    for (const [code, x, y] of [["ESTTOKYO", 227, 260], ["ESTNAHA", 107, 147], ["ESTEDGE", 154, 56]]) {
      for (const scale of [1, 3, 5, 10]) {
        safetyDraws = 0;
        window.editorTest.select(code);
        window.editorTest.setZoom(scale);
        await tick();
        assert(info.textContent.includes("估算中心") && info.textContent.includes(`${x}, ${y}`), "Missing approximate coordinate value");
        assert(safety.textContent === "" && safetyDraws === 0, "An estimate displayed the 3x3 safety square");
        const center = context.getTransform().transformPoint({ x: x + 0.5, y: y + 0.5 });
        assert(Math.abs(center.x - canvas.width / 2) < 1 && Math.abs(center.y - canvas.height / 2) < 1,
          "Centering applied image/container scaling twice");
        assert(isBlue(center.x, center.y), "Estimated center is absent from the canvas");
        assert(document.getElementById("clear").disabled, "An estimate was treated as a saved coordinate");
      }
    }
    for (const code of ["ESTOUT", "ESTMAIN", "ESTINSET"]) {
      safetyDraws = 0;
      window.editorTest.select(code);
      await tick();
      assert(!hasBlue() && safetyDraws === 0, "An invalid/out-of-image estimate was drawn or clamped");
      assert(info.textContent.includes(code === "ESTOUT" ? "超出底图范围" : "无法估算中心"), "Missing invalid-region explanation");
    }
    window.editorTest.select("ESTKNOWN");
    safetyDraws = 0;
    await tick();
    assert(info.textContent.includes("200, 200") && !info.textContent.includes("估算中心"), "An estimate replaced a saved coordinate");
    assert(safetyDraws > 0 && safety.textContent.includes("3×3"), "Actual-coordinate safety square was changed");
    window.editorTest.select("ESTTOKYO");
    window.editorTest.setZoom(3);
    await tick();
    const before = context.getTransform().transformPoint({ x: 227.5, y: 260.5 });
    window.verifyImageMarkerPan = async (dx, dy) => {
      await tick();
      const after = context.getTransform().transformPoint({ x: 227.5, y: 260.5 });
      assert(Math.abs(after.x - before.x - dx * devicePixelRatio) < 1
        && Math.abs(after.y - before.y - dy * devicePixelRatio) < 1, "Marker ignored the image pan offset");
      assert(isBlue(after.x, after.y), "Panned marker no longer follows the displayed image");
      return true;
    };
    return true;
  } finally { context.strokeRect = strokeRect; }
})()
