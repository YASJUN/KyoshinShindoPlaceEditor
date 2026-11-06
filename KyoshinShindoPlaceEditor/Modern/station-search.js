"use strict";
(() => {
  const coordinates = typeof module !== "undefined" && module.exports ? require("./image-coordinates.js") : window.ImageCoordinates;
  const radians = Math.PI / 180;
  // Inverse of the editor's zoom-5 Mercator map, whose origin is tile (26, 10).
  function mapLocation(x, y) {
    return { lon: (x + 26 * 256) / 8192 * 360 - 180,
      lat: Math.atan(Math.sinh(Math.PI * (1 - 2 * (y + 10 * 256) / 8192))) / radians };
  }
  function kilometers(a, b) {
    const h = Math.sin((b.lat - a.lat) * radians / 2) ** 2
      + Math.cos(a.lat * radians) * Math.cos(b.lat * radians) * Math.sin((b.lon - a.lon) * radians / 2) ** 2;
    return 6371.0088 * 2 * Math.asin(Math.sqrt(Math.max(0, Math.min(1, h))));
  }
  function search(points, query) {
    if (!query || !["image", "map"].includes(query.space) || !["nearest", "nearby"].includes(query.mode)
      || !Number.isFinite(query.x) || !Number.isFinite(query.y)
      || (query.mode === "nearby" && (!Number.isFinite(query.radius) || query.radius <= 0))) return [];
    const origin = query.space === "map" ? mapLocation(query.x, query.y) : null;
    const ranked = [];
    for (const point of points) {
      if (query.mode === "nearest" && (point.pixel || point.suspended)) continue;
      let distance;
      if (origin) {
        if (!Number.isFinite(point.lat) || !Number.isFinite(point.lon) || Math.abs(point.lat) > 90 || Math.abs(point.lon) > 180) continue;
        distance = kilometers(origin, point);
      } else {
        let pixel = point.pixel || point.draft;
        if (!pixel) {
          const estimate = coordinates.latLonToImage(point.lat, point.lon);
          if (coordinates.isInsideImage(estimate)) pixel = [estimate.x, estimate.y];
        }
        if (!pixel || !coordinates.isInsideImage({ x: pixel[0], y: pixel[1] })) continue;
        distance = Math.hypot(pixel[0] + .5 - query.x, pixel[1] + .5 - query.y);
      }
      if (Number.isFinite(distance)) ranked.push({ point, distance });
    }
    ranked.sort((a, b) => a.distance - b.distance || a.point.code.localeCompare(b.point.code));
    const limit = query.mode === "nearest" ? ranked[0]?.distance : query.radius;
    return ranked.filter(item => item.distance <= limit + 1e-8);
  }
  const api = Object.freeze({ search, mapLocation, kilometers });
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else window.StationSearch = api;
})();
