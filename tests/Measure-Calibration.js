"use strict";
const fs = require("node:fs");
const controls = require("./fixtures/calibration-stations.json");
const rows = JSON.parse(fs.readFileSync(process.argv[2], "utf8").replace(/^\uFEFF/, ""));
const excluded = new Set(controls.map(p => p.code));
function fit(rows, source, target) {
  const mx = rows.reduce((s,p)=>s+p.location[source],0)/rows.length;
  const my = rows.reduce((s,p)=>s+p.point.center_point[target],0)/rows.length;
  const slope = rows.reduce((s,p)=>s+(p.location[source]-mx)*(p.point.center_point[target]-my),0)
    / rows.reduce((s,p)=>s+(p.location[source]-mx)**2,0);
  return [slope,my-slope*mx];
}
for (const main of [true,false]) {
  const initial=rows.filter(p=>!p.is_suspended && p.point && (p.location.latitude>=29.9)===main && !excluded.has(p.code));
  let retained=initial, x, y;
  for(let i=0;i<4;i++) {
    x=fit(retained,"longitude","x"); y=fit(retained,"latitude","y");
    retained=initial.filter(p=>Math.abs(x[0]*p.location.longitude+x[1]-p.point.center_point.x)<1.25
      && Math.abs(y[0]*p.location.latitude+y[1]-p.point.center_point.y)<1.25);
  }
  const validation=controls.filter(p=>(p.latitude>=29.9)===main);
  const errors=validation.map(p=>(Math.round(x[0]*p.longitude+x[1])-p.observed[0])**2+(Math.round(y[0]*p.latitude+y[1])-p.observed[1])**2);
  console.log({region:main?"main":"inset",training:initial.length,retained:retained.length,x,y,validation:validation.length,
    rms:Math.sqrt(errors.reduce((a,b)=>a+b,0)/errors.length),exact:errors.filter(e=>e===0).length});
}
