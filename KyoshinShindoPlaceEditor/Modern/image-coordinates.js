"use strict";
(() => {
  // Fitted independently for the main map and inset. See CALIBRATION.md.
  const mainProjection = { sx: 20.35281411, tx: -2617.89823450, sy: -24.54181987, ty: 1134.80999166 };
  const insetProjection = { sx: 20.37291523, tx: -2496.24370754, sy: -24.56097072, ty: 786.83014391 };
  // Fixed 352x400 NIED layout, including its southwest-island inset; not Mercator.
  function latLonToImage(latitude, longitude) {
    if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;
    const main = latitude >= 29.9;
    const projection = main ? mainProjection : insetProjection;
    const x = Math.round(projection.sx * longitude + projection.tx);
    const y = Math.round(projection.sy * latitude + projection.ty);
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
    const projection = inset ? insetProjection : mainProjection;
    const latitude = (y - projection.ty) / projection.sy;
    const longitude = (x - projection.tx) / projection.sx;
    if (inset ? latitude >= 29.9 : latitude < 29.9) return null;
    return { latitude, longitude };
  }

  const api = Object.freeze({ latLonToImage, imageToLatLon, isInsideImage });
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else window.ImageCoordinates = api;
})();
