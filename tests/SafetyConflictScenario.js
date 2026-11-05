(async () => {
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  const tick = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  const legend = document.getElementById("conflicts");
  const rows = () => [...document.querySelectorAll(".station-row")];
  const items = () => [...legend.querySelectorAll(".conflict-station")];
  const color = element => element.style.getPropertyValue("--conflict-color").trim();
  const expectedCodes = ["A", "B", "C", ...Array.from({ length: 11 }, (_, i) => "DUP" + String(i).padStart(2, "0"))];
  const search = document.getElementById("search");
  const sample = document.createElement("canvas").getContext("2d");
  function luminance(cssColor) {
    sample.fillStyle = cssColor;
    sample.fillRect(0, 0, 1, 1);
    const rgb = [...sample.getImageData(0, 0, 1, 1).data].slice(0, 3).map(channel => {
      const value = channel / 255;
      return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
    });
    return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
  }
  function filter(value) {
    search.value = value;
    search.dispatchEvent(new Event("input"));
  }
  function checkRows() {
    for (const row of rows()) {
      const entry = items().find(item => item.dataset.code === row.dataset.code);
      assert(row.classList.contains("conflicting") === !!entry, "Row has stale/missing conflict color: " + row.dataset.code);
      assert(color(row) === (entry ? color(entry) : ""), "Row/legend colors differ: " + row.dataset.code);
      if (entry) {
        const foreground = luminance(color(entry)), background = luminance(getComputedStyle(row).backgroundColor);
        assert((background + 0.05) / (foreground + 0.05) >= 4.5, "Conflict text has insufficient contrast: " + row.dataset.code);
      }
    }
  }
  function mapColors(id) {
    const canvas = document.getElementById(id), context = canvas.getContext("2d");
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
    const sample = document.createElement("canvas").getContext("2d");
    const expected = new Map(), found = new Set();
    for (const entry of items()) {
      sample.clearRect(0, 0, 1, 1);
      sample.fillStyle = color(entry);
      sample.fillRect(0, 0, 1, 1);
      const rgb = sample.getImageData(0, 0, 1, 1).data;
      expected.set((rgb[0] << 16) | (rgb[1] << 8) | rgb[2], entry.dataset.code);
    }
    for (let index = 0; index < pixels.length && found.size < expected.size; index += 4) {
      const rgb = (pixels[index] << 16) | (pixels[index + 1] << 8) | pixels[index + 2];
      if (expected.has(rgb)) found.add(rgb);
    }
    for (const [rgb, code] of expected)
      assert(found.has(rgb), "Map lacks conflict color for " + code + " on " + id);
  }
  document.getElementById("notice").hidden = true;
  window.editorTest.select("ROOT");
  await tick();
  assert(items().map(item => item.dataset.code).join() === expectedCodes.join(), "Conflict legend lacks station names or includes a point outside 3x3");
  assert(items().every(item => item.textContent.includes("安全区测试站 " + item.dataset.code)), "Station names are only in a tooltip");
  const originalColors = items().map(color);
  assert(new Set(originalColors).size === expectedCodes.length, "Palette reused a color within this safety area");
  checkRows();
  assert(rows().some(row => row.dataset.code === "ROOT" && row.classList.contains("selected") && !row.classList.contains("conflicting")),
    "The current station lost selection styling or was counted as its own conflict");
  for (const scale of [1, 3, 5]) {
    window.editorTest.setZoom(scale);
    await tick();
    mapColors("monitor");
    mapColors("geographic");
  }
  const viewport = document.getElementById("table-scroll");
  viewport.scrollTop = viewport.scrollHeight;
  await tick();
  checkRows();
  viewport.scrollTop = 0;
  await tick();
  checkRows();
  filter("DUP");
  await tick();
  checkRows();
  assert(items().map(color).join() === originalColors.join(), "Filtering reassigned conflict colors");
  filter("");
  await tick();
  for (const code of ["EMPTY", "UNSET"]) {
    window.editorTest.select(code);
    await tick();
    assert(items().length === 0 && !rows().some(row => row.classList.contains("conflicting") || color(row)),
      "Switching to an empty/estimated station left stale colors");
  }
  window.editorTest.select("ROOT");
  window.editorTest.setZoom(5);
  await tick();
  assert(items().map(color).join() === originalColors.join(), "Returning to a selection changed the palette");
  window.verifySafetyConflictLayout = async () => {
    await tick();
    const footer = document.querySelector("footer").getBoundingClientRect();
    assert(footer.left >= 0 && footer.right <= innerWidth + 1 && footer.bottom <= innerHeight + 1,
      "Long conflict names pushed the status bar outside the window");
    assert(getComputedStyle(legend).overflowY === "auto", "Long conflict lists cannot scroll");
    assert(legend.scrollHeight <= legend.clientHeight || legend.clientHeight <= 96, "Long legend lost its height limit");
    checkRows();
    return true;
  };
  await window.verifySafetyConflictLayout();
  window.verifySafetyConflictEdits = async () => {
    await window.editorTest.command("edit", { code: "B", x: 240, y: 240 });
    await tick();
    assert(!items().some(item => item.dataset.code === "B"), "Editing a point out of the area left its color");
    await window.editorTest.command("undo");
    window.editorTest.select("ROOT");
    await tick();
    assert(items().map(color).join() === originalColors.join(), "Undo did not restore conflict colors");
    checkRows();
    await window.editorTest.command("sort");
    window.editorTest.select("ROOT");
    await tick();
    assert(items().map(color).join() === originalColors.join(), "Sorting reassigned conflict colors");
    checkRows();
    await window.editorTest.command("undo");
    window.editorTest.select("ROOT");
    await tick();
    assert(items().map(color).join() === originalColors.join(), "Undoing sorting lost conflict colors");
    return true;
  };
  return true;
})()
