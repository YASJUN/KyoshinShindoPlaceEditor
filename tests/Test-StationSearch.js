"use strict";
const assert = require("node:assert/strict");
const { test } = require("node:test");
const { search, mapLocation, kilometers } = require("../KyoshinShindoPlaceEditor/Modern/station-search.js");
const query = { space: "image", x: 100.5, y: 100.5, mode: "nearest", radius: 10 };
test("nearest excludes marked/suspended and retains every equidistant station", () => {
  const points = [{ code: "marked", pixel: [100,100] }, { code: "stopped", draft: [100,100], suspended: true },
    { code: "B", draft: [101,100] }, { code: "A", draft: [99,100] }, { code: "far", draft: [102,100] }];
  assert.deepEqual(search(points, query).map(item => item.point.code), ["A", "B"]);
});
test("nearby includes marked and stopped, orders distance, and includes radius boundary", () => {
  const points = [{ code: "outer", pixel: [103,104] }, { code: "same", pixel: [100,100], suspended: true }, { code: "out", pixel: [106,100] }];
  assert.deepEqual(search(points, { ...query, mode: "nearby", radius: 5 }).map(item => [item.point.code, item.distance]), [["same",0],["outer",5]]);
});
test("image uses saved pixel then draft then calibrated estimate without modifying records", () => {
  const points = [{ code: "saved", pixel: [105,143], draft: [1,1], lat:43,lon:141 },
    { code:"draft", draft:[105,143], lat:43,lon:141 }, { code:"estimated", lat:26.2124,lon:127.6809 },
    { code:"invalid", lat:null,lon:null }];
  const original = JSON.stringify(points);
  assert.equal(search(points, { ...query, x:105.5,y:143.5,mode:"nearby",radius:.1 }).length, 3);
  assert.equal(JSON.stringify(points), original);
});
test("map uses official latitude/longitude irrespective of image and draft offsets", () => {
  const origin = mapLocation(400,600);
  const points = [{ code:"A", ...origin, pixel:[1,1] }, { code:"B",lat:origin.lat+.1,lon:origin.lon, draft:[1,1] }];
  assert.equal(search(points, {space:"map",mode:"nearby",x:400,y:600,radius:10}).length,1);
  assert.deepEqual(search(points, {space:"map",mode:"nearest",x:400,y:600}).map(item=>item.point.code),["B"]);
  assert.ok(Math.abs(kilometers(origin,points[1])-11.119508)<.00001);
});
test("Mercator inverse matches equator and longitude at known world pixel", () => {
  assert.deepEqual(mapLocation(4096-26*256,4096-10*256),{lat:0,lon:0});
});
test("empty, missing and invalid coordinates do not produce candidates", () => {
  assert.deepEqual(search([],query),[]);
  assert.deepEqual(search([{code:"null",lat:null,lon:null},{code:"outside",pixel:[352,400]}],query),[]);
  assert.deepEqual(search([{code:"bad",lat:100,lon:10}],{...query,space:"map"}),[]);
  for (const patch of [{x:NaN},{radius:0,mode:"nearby"},{space:"bad"},{mode:"bad"}])
    assert.deepEqual(search([{code:"A",pixel:[100,100]}],{...query,...patch}),[]);
});
