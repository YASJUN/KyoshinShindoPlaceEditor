"use strict";
(() => {
  // Fixed 352x400 NIED layout, including its southwest-island inset; not Mercator.
  function latLonToImage(latitude, longitude) {
    if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;
    const main = latitude >= 29.9;
    const x = Math.round(main
      ? 20.2053 * (longitude - 129.74) + 24 + (longitude - 140) / 20
      : 20.3735 * (longitude - 123.8) + 28);
    const y = Math.round(main
      ? -24.5517 * (latitude - 31) + 374.5
      : -24.7031 * (latitude - 24.32) + 193.7);
    const inInset = x < 172 && y < 208 && y < (-73 / 62) * (x - 110) + 208;
    if (main ? inInset : !inInset) return null;
    return { x, y };
  }

  function isInsideImage(point) {
    return point != null && Number.isFinite(point.x) && Number.isFinite(point.y)
      && point.x >= 0 && point.x < 352 && point.y >= 0 && point.y < 400;
  }

  // Invert the unrounded formulas at a pixel's coordinate; rasterization loses precision.
  function imageToLatLon(x, y) {
    if (!isInsideImage({ x, y })) return null;
    const inset = x < 172 && y < 208 && y < (-73 / 62) * (x - 110) + 208;
    const latitude = inset ? 24.32 + (193.7 - y) / 24.7031 : 31 + (374.5 - y) / 24.5517;
    const longitude = inset ? 123.8 + (x - 28) / 20.3735
      : (x - 24 + 20.2053 * 129.74 + 140 / 20) / (20.2053 + 1 / 20);
    if (inset ? latitude >= 29.9 : latitude < 29.9) return null;
    return { latitude, longitude };
  }

  const api = Object.freeze({ latLonToImage, imageToLatLon, isInsideImage });
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else window.ImageCoordinates = api;
})();
