"use strict";
const assert = require("node:assert/strict");
const { test } = require("node:test");
const { WIDTH, HEIGHT, observedMask, compare } = require("../KyoshinShindoPlaceEditor/Modern/image-comparison");
const point = (code, x, y, extra = {}) => ({ code, name: code, pixel: [x,y], type: "K-NET", suspended: false, ...extra });
const empty = () => new Uint8Array(WIDTH * HEIGHT);
test("picking respects pixel centers, zoom and deterministic overlapping candidates", () => {
  const { hitTest } = require("../KyoshinShindoPlaceEditor/Modern/image-comparison");
  const points = [point("B",20,20),point("A",20,20),point("C",24,20)];
  assert.deepEqual(hitTest(points,20.5,20.5,10).map(p=>p.code),["A","B"]);
  assert.deepEqual(hitTest(points,20.5,20.5,1).map(p=>p.code),["A","B","C"]);
  assert.equal(hitTest(points,100,100,1).length,0);
  assert.equal(hitTest(points,21.4,21.4,10).length,2);
});
function square(mask, x, y) { for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) mask[(y+dy)*WIDTH+x+dx] = 1; return mask; }
test("transparent pixels, black text and gray map are not stations; intensity changes preserve presence", () => {
  const rgba = new Uint8ClampedArray(WIDTH*HEIGHT*4);
  [[0,0,205,255],[255,0,0,255],[255,255,0,255],[0,255,0,255],[0,0,205,0],[0,0,0,255],[190,190,190,255]].forEach((p,i)=>rgba.set(p,i*4));
  assert.deepEqual([...observedMask(rgba).slice(0,7)], [1,1,1,1,0,0,0]);
});
test("unchanged square has nine shared pixels, no additions or missing stations", () => {
  const r = compare([point("A",20,20)],square(empty(),20,20));
  assert.deepEqual(r.counts,{missing:0,added:0,matched:9}); assert.equal(r.missingStations.length,0); assert.equal(r.additions.length,0);
});
test("one pixel movement leaves opposite missing and added edges", () => {
  const r = compare([point("A",20,20)],square(empty(),21,20));
  assert.deepEqual(r.counts,{missing:3,added:3,matched:6});
  assert.equal(r.labels[20*WIDTH+19],1); assert.equal(r.labels[20*WIDTH+22],2);
  assert.deepEqual(r.additions,[{x:22,y:20,count:3}]);
});
test("new and missing sites are separated from intensity changes", () => {
  const r = compare([point("A",20,20)],square(empty(),50,50));
  assert.deepEqual(r.counts,{missing:9,added:9,matched:0}); assert.equal(r.missingStations[0].code,"A");
});
test("drafts, absent coordinates and suspended stations are excluded; borehole excludes K-NET", () => {
  const rows=[point("K",20,20),point("B",40,40,{type:"KiK-net"}),point("S",60,60,{suspended:true,type:"KiK-net"}),{code:"D",pixel:null,draft:[80,80]}];
  assert.equal(compare(rows,empty()).stations.length,2);
  assert.equal(compare(rows,empty(),{source:"borehole"}).stations.length,1);
  assert.equal(compare(rows,empty(),{source:"borehole",includeSuspended:true}).stations.length,2);
});
test("overlapping sites do not double count pixels; edge squares do not wrap", () => {
  assert.equal(compare([point("A",20,20),point("B",20,20)],empty()).counts.missing,9);
  const r = compare([point("A",0,0)],empty()); assert.equal(r.counts.missing,4); assert.equal(r.labels[WIDTH-1],0);
});
test("single-pixel mode uses actual pick position; invalid pixels are excluded", () => {
  const r=compare([point("A",10,20),point("BAD",352,20),point("N",NaN,2)],empty(),{radius:0});
  assert.equal(r.counts.missing,1); assert.equal(r.labels[20*WIDTH+10],1);
});
