// Cytoscape.js graph wrapper for MLQT dependency visualization.
// All functions are window-scoped globals to work with Blazor JS interop (no ES modules).
// Requires scripts loaded in this order: cytoscape.min.js, dagre.min.js, cytoscape-dagre.js,
// klayjs.js, cytoscape-klay.js, weaver.min.js, cytoscape-spread.js,
// layout-base.js, cose-base.js, cytoscape-fcose.js.

window._cytoscapeInstances = {};

// Resolves a CSS variable reference like "var(--mud-palette-primary)" to its computed value.
// Cytoscape renders on canvas and cannot read CSS variables itself, so we resolve them here.
window._resolveCssVar = function (value) {
    if (!value || value.indexOf('var(') !== 0) return value;
    var varName = value.slice(4, -1).trim();
    var resolved = getComputedStyle(document.documentElement).getPropertyValue(varName).trim();
    return resolved || value;
};

// Resolves color and borderColor CSS variables in node element data before passing to Cytoscape.
window._resolveElementColors = function (elements) {
    return elements.map(function (el) {
        if (el.group !== 'nodes') return el;
        return {
            group: el.group,
            data: Object.assign({}, el.data, {
                color: window._resolveCssVar(el.data.color),
                borderColor: window._resolveCssVar(el.data.borderColor)
            })
        };
    });
};

window.cytoscapeGraph = {

    // Returns a Cytoscape layout config object for the given layout name.
    _getLayoutConfig: function (layoutName) {
        switch (layoutName) {
            case 'dagre-lr':
                return { name: 'dagre', rankDir: 'LR', nodeSep: 40, rankSep: 100, padding: 20, animate: false };
            case 'breadthfirst':
                return { name: 'breadthfirst', directed: true, padding: 20, spacingFactor: 1.25, animate: false };
            case 'concentric':
                return {
                    name: 'concentric', padding: 30, minNodeSpacing: 50, animate: false,
                    concentric: function (node) { return node.degree(); },
                    levelWidth: function () { return 2; }
                };
            case 'cose':
                return { name: 'cose', padding: 30, animate: false, nodeRepulsion: 4096, edgeElasticity: 100, gravity: 1 };
            case 'circle':
                return { name: 'circle', padding: 30, animate: false };
            case 'grid':
                return { name: 'grid', padding: 20, animate: false };
            case 'spread':
                return { name: 'spread', padding: 30, animate: false, minDist: 60, expandingFactor: 1.0 };
            case 'klay':
                return {
                    name: 'klay', padding: 20, animate: false,
                    klay: { direction: 'DOWN', spacing: 50, edgeSpacingFactor: 0.5, nodePlacement: 'BRANDES_KOEPF' }
                };
            case 'fcose':
                return { name: 'fcose', padding: 30, animate: false, nodeSeparation: 75, quality: 'proof', randomize: false };
            case 'dagre-tb':
            default:
                return { name: 'dagre', rankDir: 'TB', nodeSep: 60, rankSep: 80, padding: 20, animate: false };
        }
    },

    init: function (containerId, elements, dotNetRef, layoutName) {
        window.cytoscapeGraph.destroy(containerId);

        var container = document.getElementById(containerId);
        if (!container) return;

        var layout = layoutName || 'dagre-tb';

        var cy = cytoscape({
            container: container,
            elements: window._resolveElementColors(elements),
            style: [
                {
                    selector: 'node',
                    style: {
                        'background-color': 'data(color)',
                        'border-color': 'data(borderColor)',
                        'border-width': 2,
                        'label': 'data(label)',
                        'color': '#ffffff',
                        'font-size': '9px',
                        'font-weight': 'bold',
                        'text-valign': 'center',
                        'text-halign': 'center',
                        'width': 40,
                        'height': 40,
                        'text-wrap': 'wrap',
                        'text-max-width': '36px'
                    }
                },
                {
                    selector: 'edge',
                    style: {
                        'width': 1.5,
                        'line-color': '#cccccc',
                        'target-arrow-color': '#cccccc',
                        'target-arrow-shape': 'triangle',
                        'curve-style': 'bezier'
                    }
                },
                {
                    selector: 'node.dimmed',
                    style: { 'opacity': 0.2 }
                },
                {
                    selector: 'edge.dimmed',
                    style: { 'opacity': 0.1 }
                },
                {
                    selector: 'node.highlighted',
                    style: {
                        'border-width': 3,
                        'border-color': '#000000',
                        'width': 48,
                        'height': 48
                    }
                },
                {
                    selector: 'edge.highlighted',
                    style: {
                        'width': 2.5,
                        'line-color': '#1976d2',
                        'target-arrow-color': '#1976d2'
                    }
                }
            ],
            layout: window.cytoscapeGraph._getLayoutConfig(layout),
            userZoomingEnabled: true,
            userPanningEnabled: true,
            boxSelectionEnabled: false
        });

        cy.on('tap', 'node', function (evt) {
            dotNetRef.invokeMethodAsync('OnNodeClickedFromJs', evt.target.id());
        });

        cy.on('tap', function (evt) {
            if (evt.target === cy) {
                dotNetRef.invokeMethodAsync('OnBackgroundClickedFromJs');
            }
        });

        window._cytoscapeInstances[containerId] = { cy: cy, layoutName: layout, dotNetRef: dotNetRef };
    },

    update: function (containerId, elements) {
        var inst = window._cytoscapeInstances[containerId];
        if (!inst) return;
        inst.cy.elements().remove();
        inst.cy.add(window._resolveElementColors(elements));
        var layoutCfg = window.cytoscapeGraph._getLayoutConfig(inst.layoutName);
        var l = inst.cy.layout(layoutCfg);
        l.on('layoutstop', function () { inst.cy.fit(undefined, 20); });
        l.run();
    },

    relayout: function (containerId, layoutName) {
        var inst = window._cytoscapeInstances[containerId];
        if (!inst) return;
        inst.layoutName = layoutName;
        inst.cy.elements().removeClass('highlighted dimmed');
        var layoutCfg = window.cytoscapeGraph._getLayoutConfig(layoutName);
        var l = inst.cy.layout(layoutCfg);
        l.on('layoutstop', function () { inst.cy.fit(undefined, 20); });
        l.run();
    },

    highlight: function (containerId, nodeId) {
        var inst = window._cytoscapeInstances[containerId];
        if (!inst) return;
        var cy = inst.cy;
        cy.elements().removeClass('highlighted dimmed');
        var node = cy.getElementById(nodeId);
        if (!node || node.empty()) return;
        var neighbourhood = node.closedNeighborhood();
        cy.elements().not(neighbourhood).addClass('dimmed');
        node.addClass('highlighted');
        node.connectedEdges().not('.dimmed').addClass('highlighted');
    },

    clearHighlight: function (containerId) {
        var inst = window._cytoscapeInstances[containerId];
        if (inst) inst.cy.elements().removeClass('highlighted dimmed');
    },

    destroy: function (containerId) {
        var inst = window._cytoscapeInstances[containerId];
        if (inst) {
            inst.cy.destroy();
            delete window._cytoscapeInstances[containerId];
        }
    }
};
