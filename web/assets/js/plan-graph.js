/* Query Doctor AI — Motor de layout gráfico para árboles Showplan y formateadores de series (CSP 'self'). */
(function (root, factory) {
  var api = factory();
  if (typeof module === "object" && module && module.exports) {
    module.exports = api;
  }
  if (root) {
    root.QDPlanGraph = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : window, function () {
  "use strict";

  var SVG_NS = "http://www.w3.org/2000/svg";
  var NODE_W = 216;
  var NODE_H = 104;
  var COL_GAP = 64;
  var ROW_GAP = 24;
  var PAD_X = 24;
  var PAD_Y = 24;

  function operatorCategory(physicalOp, logicalOp) {
    var name = String(physicalOp || logicalOp || "").toLowerCase();
    if (name.indexOf("key lookup") !== -1 || name.indexOf("rid lookup") !== -1) { return "lookup"; }
    if (name.indexOf("seek") !== -1) { return "seek"; }
    if (name.indexOf("scan") !== -1) { return "scan"; }
    if (name.indexOf("sort") !== -1 || name.indexOf("top") !== -1) { return "sort"; }
    if (name.indexOf("hash") !== -1) { return "hash"; }
    if (name.indexOf("nested") !== -1 || name.indexOf("merge") !== -1 || name.indexOf("join") !== -1) { return "join"; }
    if (name.indexOf("aggregate") !== -1 || name.indexOf("stream") !== -1) { return "aggregate"; }
    if (name.indexOf("parallel") !== -1) { return "parallel"; }
    if (name.indexOf("select") !== -1 || name.indexOf("insert") !== -1 || name.indexOf("update") !== -1 || name.indexOf("delete") !== -1) { return "root"; }
    return "compute";
  }

  function formatDurationMicroseconds(us) {
    if (typeof us !== "number" || isNaN(us)) { return "—"; }
    var abs = Math.abs(us);
    if (abs >= 1000000) {
      return (us / 1000000).toLocaleString("es-CO", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " s";
    }
    if (abs >= 1000) {
      return (us / 1000).toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + " ms";
    }
    return us.toLocaleString("es-CO", { minimumFractionDigits: 0, maximumFractionDigits: 0 }) + " µs";
  }

  function formatCompactCount(value) {
    if (typeof value !== "number" || isNaN(value)) { return "—"; }
    var abs = Math.abs(value);
    if (abs >= 1000000) {
      return (value / 1000000).toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + "M";
    }
    if (abs >= 1000) {
      return (value / 1000).toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + "k";
    }
    return value.toLocaleString("es-CO", { minimumFractionDigits: 0, maximumFractionDigits: 0 });
  }

  /**
   * Calcula coordenadas (x, y) sin solapamientos para un árbol de operadores.
   * Las hojas ocupan franjas verticales exclusivas; cada nodo padre queda centrado entre sus hijos.
   */
  function computeTreeLayout(operators, zoomScale) {
    var list = Array.isArray(operators) ? operators : [];
    var z = typeof zoomScale === "number" && zoomScale > 0 ? zoomScale : 1;
    var nodeW = Math.round(NODE_W * z);
    var nodeH = Math.round(NODE_H * z);
    var colStep = Math.round((NODE_W + COL_GAP) * z);
    var rowStep = Math.round((NODE_H + ROW_GAP) * z);
    var padX = Math.round(PAD_X * z);
    var padY = Math.round(PAD_Y * z);

    if (!list.length) {
      return { width: 0, height: 0, nodeW: nodeW, nodeH: nodeH, nodes: [], edges: [] };
    }

    var byId = {};
    var childrenById = {};
    var roots = [];

    list.forEach(function (op) {
      byId[op.nodeId] = op;
      childrenById[op.nodeId] = [];
    });

    list.forEach(function (op) {
      if (op.parentNodeId !== null && op.parentNodeId !== undefined && byId[op.parentNodeId]) {
        childrenById[op.parentNodeId].push(op);
      } else {
        roots.push(op);
      }
    });

    var slotCounter = 0;
    var placements = {};
    var maxCol = 0;
    var visited = {};

    function assign(op, col) {
      if (visited[op.nodeId]) { return placements[op.nodeId].yCenter; }
      visited[op.nodeId] = true;
      if (col > maxCol) { maxCol = col; }

      var children = childrenById[op.nodeId] || [];
      var yTop;
      if (!children.length) {
        yTop = padY + slotCounter * rowStep;
        slotCounter += 1;
      } else {
        var childCenters = children.map(function (c) { return assign(c, col + 1); });
        var firstC = childCenters[0];
        var lastC = childCenters[childCenters.length - 1];
        yTop = (firstC + lastC) / 2 - nodeH / 2;
      }

      var x = padX + col * colStep;
      placements[op.nodeId] = {
        op: op,
        x: x,
        y: yTop,
        yCenter: yTop + nodeH / 2,
        category: operatorCategory(op.physicalOp, op.logicalOp)
      };
      return yTop + nodeH / 2;
    }

    roots.forEach(function (r) { assign(r, 0); });
    list.forEach(function (op) {
      if (!visited[op.nodeId]) { assign(op, op.depth || 0); }
    });

    var nodes = list.map(function (op) { return placements[op.nodeId]; });
    var edges = [];
    nodes.forEach(function (childNode) {
      var pid = childNode.op.parentNodeId;
      if (pid === null || pid === undefined || !placements[pid]) { return; }
      var parentNode = placements[pid];
      edges.push({
        fromX: parentNode.x + nodeW,
        fromY: parentNode.y + nodeH / 2,
        toX: childNode.x,
        toY: childNode.y + nodeH / 2,
        isHighlighted: Boolean(childNode.op.hasFindings || (childNode.op.warnings && childNode.op.warnings.length > 0))
      });
    });

    var maxRight = 0;
    var maxBottom = 0;
    nodes.forEach(function (n) {
      if (n.x + nodeW > maxRight) { maxRight = n.x + nodeW; }
      if (n.y + nodeH > maxBottom) { maxBottom = n.y + nodeH; }
    });

    return {
      width: maxRight + padX,
      height: maxBottom + padY,
      nodeW: nodeW,
      nodeH: nodeH,
      nodes: nodes,
      edges: edges
    };
  }

  function svgEl(doc, tag, attrs) {
    var node = doc.createElementNS(SVG_NS, tag);
    Object.keys(attrs || {}).forEach(function (k) { node.setAttribute(k, String(attrs[k])); });
    return node;
  }

  function el(doc, tag, className, text) {
    var node = doc.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = String(text); }
    return node;
  }

  function buildOperatorIcon(doc, category) {
    var svg = svgEl(doc, "svg", {
      class: "pg-op-ico",
      viewBox: "0 0 20 20",
      fill: "none",
      "stroke-width": "1.6",
      "stroke-linecap": "round",
      "stroke-linejoin": "round",
      "aria-hidden": "true"
    });
    if (category === "scan") {
      svg.appendChild(svgEl(doc, "rect", { x: "3", y: "4", width: "14", height: "12", rx: "2" }));
      svg.appendChild(svgEl(doc, "path", { d: "M3 9h14M8 9v7" }));
    } else if (category === "seek") {
      svg.appendChild(svgEl(doc, "circle", { cx: "9", cy: "9", r: "4.5" }));
      svg.appendChild(svgEl(doc, "path", { d: "M12.5 12.5L17 17" }));
    } else if (category === "lookup") {
      svg.appendChild(svgEl(doc, "circle", { cx: "7", cy: "10", r: "3" }));
      svg.appendChild(svgEl(doc, "path", { d: "M10 10h7M14.5 10v3" }));
    } else if (category === "sort") {
      svg.appendChild(svgEl(doc, "path", { d: "M6 4v12M6 16l-2.5-2.5M6 16l2.5-2.5M12 6h5M12 10h4M12 14h2" }));
    } else if (category === "join" || category === "hash") {
      svg.appendChild(svgEl(doc, "circle", { cx: "7.5", cy: "10", r: "4" }));
      svg.appendChild(svgEl(doc, "circle", { cx: "12.5", cy: "10", r: "4" }));
    } else if (category === "parallel") {
      svg.appendChild(svgEl(doc, "path", { d: "M4 7h12M4 13h12M13 4l3 3-3 3M13 10l3 3-3 3" }));
    } else {
      svg.appendChild(svgEl(doc, "rect", { x: "4", y: "5", width: "12", height: "10", rx: "2" }));
      svg.appendChild(svgEl(doc, "path", { d: "M8 10h4" }));
    }
    return svg;
  }

  function renderGraphCanvas(doc, operators, options) {
    var opts = options || {};
    var zoom = opts.zoom || 1;
    var layout = computeTreeLayout(operators, zoom);

    var scrollWrap = el(doc, "div", "pg-canvas-wrap");
    var stage = el(doc, "div", "pg-stage");
    stage.style.width = layout.width + "px";
    stage.style.height = layout.height + "px";

    var svg = svgEl(doc, "svg", {
      class: "pg-edges",
      viewBox: "0 0 " + layout.width + " " + layout.height,
      width: layout.width,
      height: layout.height,
      "aria-hidden": "true"
    });

    layout.edges.forEach(function (edge) {
      var midX = (edge.fromX + edge.toX) / 2;
      var pathD = "M " + edge.fromX + " " + edge.fromY +
        " C " + midX + " " + edge.fromY + ", " + midX + " " + edge.toY + ", " + edge.toX + " " + edge.toY;
      svg.appendChild(svgEl(doc, "path", {
        class: "pg-edge" + (edge.isHighlighted ? " is-signal" : ""),
        d: pathD
      }));
      svg.appendChild(svgEl(doc, "circle", {
        class: "pg-edge-dot" + (edge.isHighlighted ? " is-signal" : ""),
        cx: edge.toX,
        cy: edge.toY,
        r: 3
      }));
    });
    stage.appendChild(svg);

    layout.nodes.forEach(function (item) {
      var op = item.op;
      var key = opts.planId + ":" + opts.stmtIdx + ":" + op.nodeId;
      var isSelected = opts.selectedKey === key;
      var isFlagged = op.hasFindings || (op.warnings && op.warnings.length > 0);

      var btn = el(doc, "button", "pg-node" + (isFlagged ? " is-signal" : "") + (isSelected ? " is-selected" : ""));
      btn.type = "button";
      btn.setAttribute("data-plan-node-key", key);
      if (op.maxFindingSeverity) { btn.setAttribute("data-severity", op.maxFindingSeverity); }
      btn.style.left = item.x + "px";
      btn.style.top = item.y + "px";
      btn.style.width = layout.nodeW + "px";
      btn.style.height = layout.nodeH + "px";

      var top = el(doc, "div", "pg-node-top");
      var leftTop = el(doc, "span", "pg-node-title-wrap");
      leftTop.appendChild(buildOperatorIcon(doc, item.category));
      leftTop.appendChild(el(doc, "span", "pg-node-op", op.physicalOp || "Operador"));
      top.appendChild(leftTop);
      top.appendChild(el(doc, "span", "pg-node-id", "#" + op.nodeId));
      btn.appendChild(top);

      var objText = op.targetObject || (op.logicalOp && op.logicalOp !== op.physicalOp ? op.logicalOp : "Operador del flujo");
      var sub = el(doc, "div", "pg-node-obj", objText);
      sub.setAttribute("title", objText);
      btn.appendChild(sub);

      var bottom = el(doc, "div", "pg-node-foot");
      var costPct = typeof op.costSharePct === "number" ? Math.max(0, Math.min(100, op.costSharePct)) : 0;
      var barWrap = el(doc, "div", "pg-cost-wrap");
      var track = el(doc, "span", "pg-cost-track");
      var fill = el(doc, "span", "pg-cost-fill");
      fill.style.width = costPct.toFixed(1) + "%";
      track.appendChild(fill);
      barWrap.appendChild(track);
      barWrap.appendChild(el(doc, "span", "pg-cost-val", costPct.toFixed(1) + "%"));
      bottom.appendChild(barWrap);

      var flags = el(doc, "div", "pg-node-flags");
      if (op.hasFindings && op.findings && op.findings.length) {
        flags.appendChild(el(doc, "span", "pg-flag-pill is-finding", op.findings[0].ruleId));
      } else if (op.warnings && op.warnings.length) {
        flags.appendChild(el(doc, "span", "pg-flag-pill is-warn", op.warnings.length + " adv."));
      }
      if (op.isParallel) {
        flags.appendChild(el(doc, "span", "pg-flag-pill", "‖"));
      }
      bottom.appendChild(flags);
      btn.appendChild(bottom);

      btn.addEventListener("click", function () {
        if (typeof opts.onSelectNode === "function") { opts.onSelectNode(op, key); }
      });

      stage.appendChild(btn);
    });

    scrollWrap.appendChild(stage);
    return scrollWrap;
  }

  return {
    operatorCategory: operatorCategory,
    formatDurationMicroseconds: formatDurationMicroseconds,
    formatCompactCount: formatCompactCount,
    computeTreeLayout: computeTreeLayout,
    renderGraphCanvas: renderGraphCanvas
  };
});
