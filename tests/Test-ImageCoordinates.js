"use strict";
const assert = require("node:assert/strict");
const { test } = require("node:test");
const { latLonToImage, imageToLatLon, isInsideImage } = require("../KyoshinShindoPlaceEditor/Modern/image-coordinates.js");

for (const [latitude, longitude, expected] of [
  [35.6812, 139.7671, { x: 227, y: 259 }],
  [34.6937, 135.5023, { x: 140, y: 283 }],
  [43.0618, 141.3545, { x: 259, y: 78 }],
  [26.2124, 127.6809, { x: 105, y: 143 }],
  [28.451, 129.6784, { x: 146, y: 88 }],
  [29.9, 130, { x: 28, y: 401 }],
  [29.8999, 130, { x: 152, y: 52 }]
]) test(`reference (${latitude}, ${longitude})`, () => assert.deepEqual(latLonToImage(latitude, longitude), expected));

test("nonfinite and nonnumeric inputs return null", () => {
  for (const value of [NaN, Infinity, -Infinity, undefined, null, "35"])
    for (const pair of [[value, 139], [35, value]])
      assert.equal(latLonToImage(...pair), null);
});

function locationForPixel(main, x, y) {
  return main
    ? [(y - 1134.80999166) / -24.54181987, (x + 2617.89823450) / 20.35281411]
    : [(y - 786.83014391) / -24.56097072, (x + 2496.24370754) / 20.37291523];
}

test("mainland points inside the inset are rejected", () => {
  assert.equal(latLonToImage(...locationForPixel(true, 100, 100)), null);
});
test("inset points outside the inset are rejected", () => {
  assert.equal(latLonToImage(...locationForPixel(false, 200, 100)), null);
});
test("x=172 is strictly outside the inset", () => {
  assert.deepEqual(latLonToImage(...locationForPixel(true, 172, 100)), { x: 172, y: 100 });
  assert.equal(latLonToImage(...locationForPixel(false, 172, 100)), null);
  assert.deepEqual(latLonToImage(...locationForPixel(false, 171, 100)), { x: 171, y: 100 });
});
test("y=208 and the diagonal endpoint are strictly outside the inset", () => {
  for (const x of [100, 110]) {
    assert.deepEqual(latLonToImage(...locationForPixel(true, x, 208)), { x, y: 208 });
    assert.equal(latLonToImage(...locationForPixel(false, x, 208)), null);
  }
});
test("adjacent pixels straddle the sloping boundary", () => {
  assert.equal(latLonToImage(...locationForPixel(true, 141, 171)), null);
  assert.deepEqual(latLonToImage(...locationForPixel(true, 141, 172)), { x: 141, y: 172 });
  assert.deepEqual(latLonToImage(...locationForPixel(false, 141, 171)), { x: 141, y: 171 });
  assert.equal(latLonToImage(...locationForPixel(false, 141, 172)), null);
});
test("rounding precedes region validation", () => {
  assert.deepEqual(latLonToImage(...locationForPixel(true, 171.6, 100)), { x: 172, y: 100 });
  assert.equal(latLonToImage(...locationForPixel(false, 171.6, 100)), null);
});
test("rounding uses the nearest calibrated pixel", () => {
  assert.deepEqual(latLonToImage(...locationForPixel(true, 230.49, 374.49)), { x: 230, y: 374 });
  assert.deepEqual(latLonToImage(...locationForPixel(true, 230.51, 374.51)), { x: 231, y: 375 });
});

test("inverse maps mainland and inset pixels to approximate geographic coordinates", () => {
  for (const [x, y, latitude, longitude] of [
    [227, 259, 35.6812, 139.7671], [140, 283, 34.6937, 135.5023],
    [259, 78, 43.0618, 141.3545], [105, 143, 26.2124, 127.6809],
    [146, 88, 28.451, 129.6784]
  ]) {
    const result = imageToLatLon(x, y);
    assert.ok(Math.abs(result.latitude - latitude) < 0.021);
    assert.ok(Math.abs(result.longitude - longitude) < 0.025);
    assert.deepEqual(latLonToImage(result.latitude, result.longitude), { x, y });
  }
});

test("conversion preserves out-of-bounds results; rendering rejects them without clamping", () => {
  assert.equal(isInsideImage(latLonToImage(29.9, 130)), false);
  for (const point of [null, { x: -1, y: 200 }, { x: 352, y: 200 }, { x: 200, y: -1 },
    { x: 200, y: 400 }, { x: Infinity, y: 10 }, { x: 10, y: NaN }]) assert.equal(isInsideImage(point), false);
  assert.equal(isInsideImage({ x: 0, y: 0 }), true);
  assert.equal(isInsideImage({ x: 351, y: 399 }), true);
});

test("independent Hokkaido and Okinawa image controls improve after calibration", () => {
  const controls = require("./fixtures/calibration-stations.json");
  for (const region of ["北海道", "沖縄県"]) {
    const rows = controls.filter(p => p.region === region);
    let before = 0, after = 0;
    for (const p of rows) {
      const main = p.latitude >= 29.9;
      const oldX = Math.round(main ? 20.2053 * (p.longitude - 129.74) + 24 + (p.longitude - 140) / 20 : 20.3735 * (p.longitude - 123.8) + 28);
      const oldY = Math.round(main ? -24.5517 * (p.latitude - 31) + 374.5 : -24.7031 * (p.latitude - 24.32) + 193.7);
      const pixel = latLonToImage(p.latitude, p.longitude);
      before += (oldX - p.observed[0]) ** 2 + (oldY - p.observed[1]) ** 2;
      const distance = (pixel.x - p.observed[0]) ** 2 + (pixel.y - p.observed[1]) ** 2;
      assert.ok(distance <= 1, p.code + " exceeds one pixel"); after += distance;
    }
    assert.ok(after < before / 4, region + " calibration did not improve");
  }
});

test("inverse rejects invalid, out-of-image and unmappable inset coordinates", () => {
  for (const value of [NaN, Infinity, -Infinity, undefined, null, "100"])
    for (const pair of [[value, 100], [100, value]])
      assert.equal(imageToLatLon(...pair), null);
  for (const pair of [[-1, 200], [352, 200], [200, -1], [200, 400], [154, 51]])
    assert.equal(imageToLatLon(...pair), null);
});

test("inverse uses the strict inset boundaries and preserves round-trip pixels", () => {
  for (const [x, y, inset] of [[171, 100, true], [172, 100, false], [110, 207, true],
    [110, 208, false], [141, 171, true], [141, 172, false]]) {
    const result = imageToLatLon(x, y);
    assert.equal(result.latitude < 29.9, inset);
    assert.deepEqual(latLonToImage(result.latitude, result.longitude), { x, y });
  }
});

test("every mapped image pixel round trips without clamping or changing regions", () => {
  for (let y = 0; y < 400; y++)
    for (let x = 0; x < 352; x++) {
      const result = imageToLatLon(x, y);
      if (result) {
        const pixel = latLonToImage(result.latitude, result.longitude);
        assert.deepEqual({ x: pixel.x + 0, y: pixel.y + 0 }, { x, y });
      }
      else assert.ok(x < 172 && y < 53, `Unexpected unmappable pixel: ${x},${y}`);
    }
});
