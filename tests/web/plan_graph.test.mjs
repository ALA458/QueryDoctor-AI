import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const pg = require("../../web/assets/js/plan-graph.js");

test("operatorCategory clasifica operadores de SQL Server correctamente", () => {
  assert.equal(pg.operatorCategory("Clustered Index Scan"), "scan");
  assert.equal(pg.operatorCategory("Index Seek"), "seek");
  assert.equal(pg.operatorCategory("Key Lookup"), "lookup");
  assert.equal(pg.operatorCategory("Nested Loops"), "join");
  assert.equal(pg.operatorCategory("Hash Match"), "hash");
  assert.equal(pg.operatorCategory("Sort"), "sort");
  assert.equal(pg.operatorCategory("Parallelism"), "parallel");
  assert.equal(pg.operatorCategory("SELECT"), "root");
});

test("formatCompactCount y formatDurationMicroseconds escalan unidades legibles", () => {
  assert.match(pg.formatDurationMicroseconds(450), /450\s*µs/);
  assert.match(pg.formatDurationMicroseconds(12500), /12,5\s*ms|12\.5\s*ms/);
  assert.match(pg.formatDurationMicroseconds(2500000), /2,50\s*s|2\.50\s*s/);
  assert.match(pg.formatCompactCount(14500), /14,5k|14\.5k/);
  assert.match(pg.formatCompactCount(2300000), /2,3M|2\.3M/);
});

test("computeTreeLayout ubica nodos ramificados sin solapamientos y genera aristas", () => {
  const operators = [
    { nodeId: 0, parentNodeId: null, depth: 0, physicalOp: "SELECT", costSharePct: 100 },
    { nodeId: 1, parentNodeId: 0, depth: 1, physicalOp: "Nested Loops", costSharePct: 95 },
    { nodeId: 2, parentNodeId: 1, depth: 2, physicalOp: "Index Seek", costSharePct: 15, targetObject: "dbo.Orders" },
    { nodeId: 3, parentNodeId: 1, depth: 2, physicalOp: "Key Lookup", costSharePct: 80, targetObject: "dbo.Orders", hasFindings: true, findings: [{ ruleId: "REG-01" }] }
  ];

  const layout = pg.computeTreeLayout(operators, 1);
  assert.equal(layout.nodes.length, 4);
  assert.equal(layout.edges.length, 3);
  assert.ok(layout.width > 0 && layout.height > 0);

  // Verificar que ninguna pareja de nodos se solape geométricamente
  for (let i = 0; i < layout.nodes.length; i++) {
    for (let j = i + 1; j < layout.nodes.length; j++) {
      const a = layout.nodes[i];
      const b = layout.nodes[j];
      const overlapX = a.x < b.x + layout.nodeW && a.x + layout.nodeW > b.x;
      const overlapY = a.y < b.y + layout.nodeH && a.y + layout.nodeH > b.y;
      assert.equal(overlapX && overlapY, false, `Nodos ${a.op.nodeId} y ${b.op.nodeId} se solapan`);
    }
  }

  // La arista hacia Key Lookup con hallazgo queda marcada como señal
  const lookupEdge = layout.edges.find((e) => e.isHighlighted);
  assert.ok(lookupEdge);
});
