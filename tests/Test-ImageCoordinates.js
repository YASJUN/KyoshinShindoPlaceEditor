"use strict";
const assert = require("node:assert/strict");
const { test } = require("node:test");
const { latLonToImage, isInsideImage } = require("../KyoshinShindoPlaceEditor/Modern/image-coordinates.js");

for (const [latitude, longitude, expected] of [
  [35.6812, 139.7671, { x: 227, y: 260 }],
  [34.6937, 135.5023, { x: 140, y: 284 }],
  [43.0618, 141.3545, { x: 259, y: 78 }],
  [26.2124, 127.6809, { x: 107, y: 147 }],
  [28.451, 129.6784, { x: 148, y: 92 }],
  [29.9, 130, { x: 29, y: 402 }],
  [29.8999, 130, { x: 154, y: 56 }]
]) test(`reference (${latitude}, ${longitude})`, () => assert.deepEqual(latLonToImage(latitude, longitude), expected));

test("nonfinite and nonnumeric inputs return null", () => {
  for (const value of [NaN, Infinity, -Infinity, undefined, null, "35"])
    for (const pair of [[value, 139], [35, value]])
      assert.equal(latLonToImage(...pair), null);
});

function locationForPixel(main, x, y) {
  return main
    ? [31 + (374.5 - y) / 24.5517, (x + 20.2053 * 129.74 - 24 + 7) / 20.2553]
    : [24.32 + (193.7 - y) / 24.7031, 123.8 + (x - 28) / 20.3735];
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
test("exact positive and negative .5 ties round toward positive infinity", () => {
  assert.deepEqual(latLonToImage(31, 140), { x: 231, y: 375 });
  assert.deepEqual(latLonToImage(20031, 140), { x: 231, y: -490659 });
});
test("conversion preserves out-of-bounds results; rendering rejects them without clamping", () => {
  assert.equal(isInsideImage(latLonToImage(29.9, 130)), false);
  for (const point of [null, { x: -1, y: 200 }, { x: 352, y: 200 }, { x: 200, y: -1 },
    { x: 200, y: 400 }, { x: Infinity, y: 10 }, { x: 10, y: NaN }])
    assert.equal(isInsideImage(point), false);
  assert.equal(isInsideImage({ x: 0, y: 0 }), true);
  assert.equal(isInsideImage({ x: 351, y: 399 }), true);
});
