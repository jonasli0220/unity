figma.showUI(__html__, { width: 380, height: 520 });

const DEFAULT_FONT = { family: 'Inter', style: 'Regular' };
const BRIDGE_NAMESPACE = 'unity.figmaBridge';
const LOCKED_FIGMA_FILE_KEY = 'eteWowFyYB3NHQWsMjI2iP';
const LOCKED_FIGMA_FILE_NAME = 'Dragon-通用组件';
const UNITY_PASTE_SCHEMA = 'dragon.figmaPaste.v2';
const UNITY_PASTE_MARKER = 'DRAGON_FIGMA_PASTE_JSON_V2';
const loadedFonts = {};
let availableFontsPromise = null;
let componentCache = {};
let createdComponents = [];

figma.ui.onmessage = async (message) => {
  if (!message) {
    return;
  }

  try {

    if (message.type === 'export-selection') {
      const payload = exportSelectionForUnity();
      figma.ui.postMessage({ type: 'export-restore-package', payload });
      return;
    }

    if (message.type === 'copy-selection-for-unity-paste') {
      const payload = await buildUnityPasteClipboardPayload();
      figma.ui.postMessage({
        type: 'unity-paste-clipboard',
        text: formatUnityPasteClipboardText(payload),
        summary: buildUnityPasteSummary(payload)
      });
      return;
    }

    if (message.type !== 'import-package') {
      return;
    }

    const payload = message.payload;
    if (!payload || !payload.manifest) {
      throw new Error('Invalid Figma Bridge package.');
    }

    assertLockedTargetFigmaFile();

    componentCache = {};
    createdComponents = [];
    if (isColorStylePackage(payload.manifest)) {
      figma.ui.postMessage({ type: 'status', text: 'Syncing color styles...' });
      const result = await syncColorStyles(payload.manifest);
      figma.ui.postMessage({ type: 'done', text: `Synced ${result.total} color styles (${result.created} created, ${result.updated} updated).` });
      return;
    }

    if (!payload.manifest.root) {
      throw new Error('Invalid Figma Bridge prefab package.');
    }

    const importTarget = await resolveImportTarget(payload.manifest, payload.options && payload.options.targetPageName);
    const statusAction = importTarget.existingRoot ? 'Updating existing Figma node' : 'Creating Figma nodes';
    figma.ui.postMessage({ type: 'status', text: `${statusAction} on ${importTarget.page.name}...` });
    const importResult = await importPrefabPackage(payload.manifest, payload.imageMap || {}, importTarget, payload.options || {});
    const root = importResult.root;
    const targetPage = importResult.page;
    const viewNodes = [root].concat(createdComponents);
    targetPage.selection = [root];
    figma.viewport.scrollAndZoomIntoView(viewNodes);

    const exportRestoreAfterImport = payload.options && payload.options.exportRestoreAfterImport;
    if (exportRestoreAfterImport) {
      figma.ui.postMessage({ type: 'export-restore-package', payload: exportRootForUnity(root) });
    }

    figma.ui.postMessage({
      type: 'done',
      text: exportRestoreAfterImport
        ? `${importResult.action === 'updated' ? 'Updated' : 'Imported'} ${root.name} on ${targetPage.name}. Restore JSON download started.`
        : `${importResult.action === 'updated' ? 'Updated' : 'Imported'} ${root.name} on ${targetPage.name}.`
    });
  } catch (error) {
    const failedAction = message && message.type === 'copy-selection-for-unity-paste'
      ? 'Copy failed'
      : 'Import failed';
    figma.ui.postMessage({ type: 'error', text: `${failedAction}:\n${error.message || error}` });
  }
};

function assertLockedTargetFigmaFile() {
  const currentKey = getCurrentFigmaFileKey();
  if (currentKey) {
    if (currentKey === LOCKED_FIGMA_FILE_KEY) {
      return;
    }

    throw new Error(buildWrongFileMessage(`key ${currentKey}`));
  }

  const currentName = getCurrentFigmaFileName();
  if (currentName) {
    if (normalizeFigmaFileName(currentName) === normalizeFigmaFileName(LOCKED_FIGMA_FILE_NAME)) {
      return;
    }

    throw new Error(buildWrongFileMessage(`文件 ${currentName}`));
  }

  throw new Error(buildWrongFileMessage('无法读取当前文件名或 file key'));
}

function getCurrentFigmaFileKey() {
  const key = typeof figma.fileKey === 'string' ? figma.fileKey : '';
  return key.trim();
}

function getCurrentFigmaFileName() {
  const names = [];
  if (figma.root && typeof figma.root.name === 'string') {
    names.push(figma.root.name);
  }
  if (figma.currentPage && figma.currentPage.parent && typeof figma.currentPage.parent.name === 'string') {
    names.push(figma.currentPage.parent.name);
  }

  for (const name of names) {
    const trimmed = String(name || '').trim();
    if (trimmed && trimmed !== 'Document') {
      return trimmed;
    }
  }

  return '';
}

function normalizeFigmaFileName(name) {
  return String(name || '').trim().replace(/\s+/g, '').toLowerCase();
}

function buildWrongFileMessage(currentFileDescription) {
  return [
    `当前插件已锁定到 Figma 文件：${LOCKED_FIGMA_FILE_NAME}`,
    `目标 file key：${LOCKED_FIGMA_FILE_KEY}`,
    `当前：${currentFileDescription}`,
    '请切换到 Dragon-通用组件 文件后重新运行插件，再从 Unity 导出或手动导入包。',
    '为避免误写入，插件不会在当前文件创建 unity组件库 页面。'
  ].join('\n');
}

async function resolveImportPage(targetPageName) {
  const name = String(targetPageName || '').trim();
  if (!name) {
    return figma.currentPage;
  }

  let page = figma.root.children.find(item => item.type === 'PAGE' && item.name === name);
  if (!page) {
    page = figma.createPage();
    page.name = name;
  }

  await setCurrentPage(page);
  return page;
}

async function resolveImportTarget(manifest, targetPageName) {
  const preferredPage = findImportPageByName(targetPageName);
  const existing = findExistingImportRoot(manifest, preferredPage);
  if (existing) {
    await setCurrentPage(existing.page);
    return { page: existing.page, existingRoot: existing.root };
  }

  const page = await resolveImportPage(targetPageName);
  return { page, existingRoot: null };
}

function findImportPageByName(targetPageName) {
  const name = String(targetPageName || '').trim();
  if (!name) {
    return figma.currentPage;
  }
  return figma.root.children.find(item => item.type === 'PAGE' && item.name === name) || null;
}

async function setCurrentPage(page) {
  if (!page || figma.currentPage === page) {
    return;
  }

  if (typeof figma.setCurrentPageAsync === 'function') {
    await figma.setCurrentPageAsync(page);
  } else {
    figma.currentPage = page;
  }
}

function findExistingImportRoot(manifest, preferredPage) {
  const selected = findSelectedImportRoot(manifest);
  if (selected) {
    return selected;
  }

  const pages = [];
  if (preferredPage) {
    pages.push(preferredPage);
  }
  if (figma.currentPage && pages.indexOf(figma.currentPage) < 0) {
    pages.push(figma.currentPage);
  }
  for (const page of figma.root.children) {
    if (page.type === 'PAGE' && pages.indexOf(page) < 0) {
      pages.push(page);
    }
  }

  for (const page of pages) {
    const match = findImportRootOnPage(page, manifest);
    if (match) {
      return { page, root: match };
    }
  }

  return null;
}

function findSelectedImportRoot(manifest) {
  const selection = figma.currentPage && figma.currentPage.selection ? figma.currentPage.selection : [];
  for (const node of selection) {
    if (matchesImportedRoot(node, manifest)) {
      return { page: figma.currentPage, root: node };
    }
  }
  return null;
}

function findImportRootOnPage(page, manifest) {
  if (!page || !('children' in page)) {
    return null;
  }

  const children = page.children.slice().reverse();
  for (const node of children) {
    if (matchesImportedRoot(node, manifest)) {
      return node;
    }
  }

  return null;
}

function matchesImportedRoot(node, manifest) {
  if (!isCompatibleRootNode(node, manifest)) {
    return false;
  }

  const existing = readPluginJson(node, 'figmaBridgeSource');
  const incoming = manifest && manifest.source ? manifest.source : {};
  if (!existing || !incoming) {
    return false;
  }

  const existingKind = String(existing.targetKind || '').toLowerCase();
  const incomingKind = String(manifest.targetKind || '').toLowerCase();
  if (existingKind && incomingKind && existingKind !== incomingKind) {
    return false;
  }

  const existingGuid = String(existing.prefabGuid || '');
  const incomingGuid = String(incoming.prefabGuid || '');
  if (existingGuid && incomingGuid && existingGuid === incomingGuid) {
    return true;
  }

  const existingPath = normalizeImportPath(existing.prefabPath || '');
  const incomingPath = normalizeImportPath(incoming.prefabPath || '');
  return !!existingPath && !!incomingPath && existingPath === incomingPath;
}

function isCompatibleRootNode(node, manifest) {
  const kind = String(manifest && manifest.targetKind || '').toLowerCase();
  if (kind === 'component') {
    return node && node.type === 'COMPONENT';
  }
  if (kind === 'frame') {
    return node && node.type === 'FRAME';
  }
  return !!node;
}

function isColorStylePackage(manifest) {
  return String(manifest.targetKind || '').toLowerCase() === 'style-color' ||
    Array.isArray(manifest.styleColors);
}

async function syncColorStyles(manifest) {
  const colors = Array.isArray(manifest.styleColors) ? manifest.styleColors : [];
  const namespace = trimSlashes(manifest.styleNamespace || 'style-color/Dragon');
  const existingStyles = await getLocalPaintStyles();
  const existingByName = {};
  for (const style of existingStyles) {
    existingByName[style.name] = style;
  }

  let created = 0;
  let updated = 0;
  for (const item of colors) {
    if (!item || !item.id) {
      continue;
    }

    const styleName = buildColorStyleName(namespace, item);
    let style = existingByName[styleName];
    if (!style) {
      style = figma.createPaintStyle();
      style.name = styleName;
      existingByName[styleName] = style;
      created++;
    } else {
      updated++;
    }

    const color = normalizeStyleColor(item);
    style.paints = [{
      type: 'SOLID',
      color: { r: color.r, g: color.g, b: color.b },
      opacity: color.a
    }];
    style.description = `Dragon Unity ColorArea: ${item.id} ${item.color || ''}`.trim();
    setStylePluginData(style, 'figmaBridgeStyleColor', {
      id: item.id || '',
      group: item.group || '',
      color: item.color || '',
      sourcePath: manifest.source ? manifest.source.sourcePath || '' : '',
      sourceHash: manifest.source ? manifest.source.sourceHash || '' : ''
    });
  }

  return { total: colors.length, created, updated };
}

async function getLocalPaintStyles() {
  if (typeof figma.getLocalPaintStylesAsync === 'function') {
    return await figma.getLocalPaintStylesAsync();
  }
  return figma.getLocalPaintStyles();
}

function buildColorStyleName(namespace, item) {
  const group = sanitizeStylePathSegment(item.group || getColorGroup(item.id));
  const name = sanitizeStylePathSegment(item.name || item.id);
  return `${namespace}/${group}/${name}`;
}

function getColorGroup(id) {
  const text = String(id || '');
  const index = text.indexOf('_');
  return index > 0 ? text.slice(0, index) : 'Ungrouped';
}

function sanitizeStylePathSegment(value) {
  return String(value || 'Unnamed').replace(/[\\/]+/g, '-').trim() || 'Unnamed';
}

function trimSlashes(value) {
  return String(value || '').replace(/^\/+|\/+$/g, '');
}

function normalizeStyleColor(item) {
  if (Number.isFinite(Number(item.r)) && Number.isFinite(Number(item.g)) && Number.isFinite(Number(item.b))) {
    return {
      r: clamp01(Number(item.r)),
      g: clamp01(Number(item.g)),
      b: clamp01(Number(item.b)),
      a: clamp01(Number.isFinite(Number(item.a)) ? Number(item.a) : 1)
    };
  }
  return parseColor(item.color || '#000000FF');
}

function clamp01(value) {
  return Math.max(0, Math.min(1, value));
}

function setStylePluginData(style, key, value) {
  if (!style) {
    return;
  }

  const serialized = JSON.stringify(value);
  if (typeof style.setSharedPluginData === 'function') {
    style.setSharedPluginData(BRIDGE_NAMESPACE, key, serialized);
    return;
  }

  if (typeof style.setPluginData === 'function') {
    style.setPluginData(key, serialized);
  }
}

async function importPrefabPackage(manifest, imageMap, importTarget, options) {
  if (importTarget.existingRoot) {
    await updateRoot(importTarget.existingRoot, manifest, imageMap, options);
    return { root: importTarget.existingRoot, page: importTarget.page, action: 'updated' };
  }

  const root = await createRoot(manifest, imageMap, options);
  importTarget.page.appendChild(root);
  placeRootOnTargetPage(root, importTarget.page);
  layoutCreatedComponents(root);
  return { root, page: importTarget.page, action: 'created' };
}

async function createRoot(manifest, imageMap, options) {
  const isComponent = String(manifest.targetKind || '').toLowerCase() === 'component';
  const root = isComponent ? figma.createComponent() : figma.createFrame();
  root.name = getRootName(manifest);

  await applyNode(root, manifest.root, null, imageMap, true);
  setBridgePluginData(root, 'figmaBridgeSource', buildRootSourceData(manifest, options));

  return root;
}

async function updateRoot(root, manifest, imageMap, options) {
  const x = numberOr(root.x, 0);
  const y = numberOr(root.y, 0);
  root.name = getRootName(manifest);
  clearNodeChildren(root);
  await applyNode(root, manifest.root, null, imageMap, true);
  root.name = getRootName(manifest);
  root.x = x;
  root.y = y;
  setBridgePluginData(root, 'figmaBridgeSource', buildRootSourceData(manifest, options));
  layoutCreatedComponents(root);
}

function clearNodeChildren(node) {
  if (!node || !('children' in node)) {
    return;
  }

  const children = node.children.slice();
  for (const child of children) {
    child.remove();
  }
}

function getRootName(manifest) {
  return manifest.source && manifest.source.prefabName ? manifest.source.prefabName : manifest.root.name;
}

function buildRootSourceData(manifest, options) {
  return {
    targetKind: manifest.targetKind,
    prefabName: manifest.source ? manifest.source.prefabName : '',
    prefabPath: manifest.source ? manifest.source.prefabPath : '',
    prefabGuid: manifest.source ? manifest.source.prefabGuid : '',
    sourceHash: manifest.source ? manifest.source.sourceHash : '',
    packageId: options ? options.packageId || '' : '',
    importSource: options ? options.source || '' : '',
    importedAt: new Date().toISOString()
  };
}

async function createChild(node, parentFrame, imageMap, parentNode, parentUnitySize, parentScaleContext) {
  if (isNestedPrefabRoot(node)) {
    return await createNestedPrefabInstance(node, parentFrame, imageMap, parentNode, parentUnitySize, parentScaleContext);
  }

  const frame = figma.createFrame();
  frame.name = node.name || 'Unity Node';
  parentFrame.appendChild(frame);
  await applyNode(frame, node, parentFrame, imageMap, false, parentNode, parentUnitySize, parentScaleContext);
  return frame;
}

async function createNestedPrefabInstance(node, parentFrame, imageMap, parentNode, parentUnitySize, parentScaleContext) {
  const source = await getOrCreateNestedComponent(node, imageMap);
  const instance = source.createInstance();
  instance.name = node.name || source.name;
  parentFrame.appendChild(instance);

  const unitySize = getNodeSize(node, parentUnitySize);
  const visualState = getNodeVisualState(node, unitySize, parentScaleContext, true);
  if (typeof instance.resize === 'function') {
    instance.resize(visualState.width, visualState.height);
  }

  if (isManagedByParentLayout(parentNode, node)) {
    applyLayoutChildSizing(instance, node, parentNode, visualState);
  } else {
    if (isIgnoredByParentLayout(parentNode, node)) {
      setAbsoluteLayoutPositioning(instance);
    }
    applyUnityTransform(instance, node, parentUnitySize, parentScaleContext, unitySize, visualState);
    applyConstraints(instance, node);
  }
  instance.visible = node.activeSelf !== false;
  setBridgeNodePluginData(instance, node, buildGraphicsPluginData(node.graphics || []));

  return instance;
}

async function getOrCreateNestedComponent(node, imageMap) {
  const ref = node.prefabReference || {};
  const key = ref.sourcePrefabGuid || ref.sourcePrefabPath || node.path || node.name;
  if (componentCache[key]) {
    return componentCache[key];
  }

  const existing = findExistingImportRoot({
    targetKind: 'Component',
    source: {
      prefabPath: ref.sourcePrefabPath || '',
      prefabGuid: ref.sourcePrefabGuid || ''
    }
  }, figma.currentPage);
  if (existing && existing.root && existing.root.type === 'COMPONENT') {
    componentCache[key] = existing.root;
    return existing.root;
  }

  const component = figma.createComponent();
  component.name = ref.sourcePrefabName || node.name || 'Unity Component';
  figma.currentPage.appendChild(component);
  await applyNode(component, node, null, imageMap, true);
  component.visible = true;
  setBridgePluginData(component, 'figmaBridgeSource', {
    targetKind: 'Component',
    prefabPath: ref.sourcePrefabPath || '',
    prefabGuid: ref.sourcePrefabGuid || '',
    sourceLocalId: ref.sourceLocalId || 0,
    instanceRootPath: ref.instanceRootPath || ''
  });

  componentCache[key] = component;
  createdComponents.push(component);
  return component;
}

function isNestedPrefabRoot(node) {
  return !!(node && node.prefabReference && node.prefabReference.isPrefabInstanceRoot);
}

function layoutCreatedComponents(root) {
  let y = root.y;
  const x = root.x + root.width + 80;
  for (const component of createdComponents) {
    component.x = x;
    component.y = y;
    y += component.height + 40;
  }
}

function placeRootOnTargetPage(root, targetPage) {
  const importedRoots = targetPage.children.filter(node => node !== root && readPluginJson(node, 'figmaBridgeSource'));
  if (importedRoots.length === 0) {
    root.x = 0;
    root.y = 0;
    return;
  }

  let maxY = -Infinity;
  for (const node of importedRoots) {
    maxY = Math.max(maxY, numberOr(node.y, 0) + numberOr(node.height, 0));
  }

  root.x = 0;
  root.y = Number.isFinite(maxY) ? Math.max(0, maxY + 80) : 0;
}

async function applyNode(frame, node, parentFrame, imageMap, isRoot, parentNode, parentUnitySize, parentScaleContext) {
  frame.clipsContent = false;
  frame.layoutMode = 'NONE';
  frame.visible = node.activeSelf !== false;

  const unitySize = getNodeSize(node, parentUnitySize || null);
  const visualState = getNodeVisualState(node, unitySize, parentScaleContext, !isRoot && !!parentFrame);
  frame.resize(visualState.width, visualState.height);

  if (!isRoot && parentFrame) {
    if (isManagedByParentLayout(parentNode, node)) {
      applyLayoutChildSizing(frame, node, parentNode, visualState);
    } else {
      if (isIgnoredByParentLayout(parentNode, node)) {
        setAbsoluteLayoutPositioning(frame);
      }
      applyUnityTransform(frame, node, parentUnitySize, parentScaleContext, unitySize, visualState);
      applyConstraints(frame, node);
    }
  }

  applyLayoutContainer(frame, node);
  applyContentSizeFitter(frame, node);

  const fillMetadata = applyFills(frame, node, imageMap, visualState);
  setBridgeNodePluginData(frame, node, fillMetadata);

  if (node.texts && node.texts.length > 0) {
    await createTextLayer(frame, node.texts[0], visualState.width, visualState.height);
  }

  const children = getLayoutChildrenInFigmaOrder(node);
  for (const child of children) {
    await createChild(child, frame, imageMap, node, unitySize, visualState.scaleContext);
  }
}

function getLayoutChildrenInFigmaOrder(node) {
  const children = (node.children || []).slice();
  const layoutGroup = getEnabledLayoutGroup(node);
  if (layoutGroup && layoutGroup.reverseArrangement === true && !isGridLayoutGroup(layoutGroup)) {
    children.reverse();
  }
  return children;
}

function applyLayoutContainer(frame, node) {
  const layoutGroup = getEnabledLayoutGroup(node);
  if (!layoutGroup) {
    return;
  }

  const type = String(layoutGroup.componentType || '');
  if (type === 'HorizontalLayoutGroup') {
    frame.layoutMode = 'HORIZONTAL';
    frame.itemSpacing = numberOr(layoutGroup.spacing, 0);
  } else if (type === 'VerticalLayoutGroup') {
    frame.layoutMode = 'VERTICAL';
    frame.itemSpacing = numberOr(layoutGroup.spacing, 0);
  } else if (type === 'GridLayoutGroup') {
    const startAxis = String(layoutGroup.startAxis || 'Horizontal');
    const gridSpacing = layoutGroup.gridSpacing || {};
    frame.layoutMode = startAxis === 'Vertical' ? 'VERTICAL' : 'HORIZONTAL';
    frame.itemSpacing = numberOr(frame.layoutMode === 'VERTICAL' ? gridSpacing.y : gridSpacing.x, 0);
    trySet(frame, 'layoutWrap', 'WRAP');
    trySet(frame, 'counterAxisSpacing', numberOr(frame.layoutMode === 'VERTICAL' ? gridSpacing.x : gridSpacing.y, 0));
  } else {
    return;
  }

  const padding = layoutGroup.padding || {};
  frame.paddingLeft = numberOr(padding.left, 0);
  frame.paddingRight = numberOr(padding.right, 0);
  frame.paddingTop = numberOr(padding.top, 0);
  frame.paddingBottom = numberOr(padding.bottom, 0);

  applyLayoutAlignment(frame, layoutGroup);
  applyContentSizeFitterToAutoLayout(frame, node);
}

function applyLayoutAlignment(frame, layoutGroup) {
  const alignment = parseUnityAlignment(layoutGroup.childAlignment || 'UpperLeft');
  if (frame.layoutMode === 'HORIZONTAL') {
    frame.primaryAxisAlignItems = alignment.horizontal;
    frame.counterAxisAlignItems = alignment.vertical;
  } else if (frame.layoutMode === 'VERTICAL') {
    frame.primaryAxisAlignItems = alignment.vertical;
    frame.counterAxisAlignItems = alignment.horizontal;
  }
}

function parseUnityAlignment(value) {
  const text = String(value || '');
  const vertical = text.indexOf('Lower') >= 0 ? 'MAX' : text.indexOf('Middle') >= 0 ? 'CENTER' : 'MIN';
  const horizontal = text.indexOf('Right') >= 0 ? 'MAX' : text.indexOf('Center') >= 0 ? 'CENTER' : 'MIN';
  return { horizontal, vertical };
}

function applyContentSizeFitter(frame, node) {
  if (!node || !node.contentSizeFitter || node.contentSizeFitter.enabled === false) {
    return;
  }

  setLayoutSizing(frame, 'horizontal', mapFitToSizing(node.contentSizeFitter.horizontalFit));
  setLayoutSizing(frame, 'vertical', mapFitToSizing(node.contentSizeFitter.verticalFit));
}

function applyContentSizeFitterToAutoLayout(frame, node) {
  if (!node || !node.contentSizeFitter || node.contentSizeFitter.enabled === false) {
    frame.primaryAxisSizingMode = 'FIXED';
    frame.counterAxisSizingMode = 'FIXED';
    return;
  }

  const horizontalFit = mapFitToAutoLayoutSizing(node.contentSizeFitter.horizontalFit);
  const verticalFit = mapFitToAutoLayoutSizing(node.contentSizeFitter.verticalFit);
  if (frame.layoutMode === 'HORIZONTAL') {
    frame.primaryAxisSizingMode = horizontalFit;
    frame.counterAxisSizingMode = verticalFit;
  } else if (frame.layoutMode === 'VERTICAL') {
    frame.primaryAxisSizingMode = verticalFit;
    frame.counterAxisSizingMode = horizontalFit;
  }
}

function applyLayoutChildSizing(figmaNode, node, parentNode, size) {
  const layoutGroup = getEnabledLayoutGroup(parentNode);
  if (!layoutGroup) {
    return;
  }

  let width = size.width;
  let height = size.height;
  if (isGridLayoutGroup(layoutGroup) && layoutGroup.cellSize) {
    width = positiveNumber(layoutGroup.cellSize.x) || width;
    height = positiveNumber(layoutGroup.cellSize.y) || height;
    resizeNode(figmaNode, width, height);
  }

  applyLayoutElementSizing(figmaNode, node.layoutElement || {}, layoutGroup, width, height);
  trySet(figmaNode, 'layoutPositioning', 'AUTO');
}

function applyLayoutElementSizing(figmaNode, layoutElement, layoutGroup, width, height) {
  const type = String(layoutGroup.componentType || '');
  const flexibleWidth = numberOr(layoutElement.flexibleWidth, -1);
  const flexibleHeight = numberOr(layoutElement.flexibleHeight, -1);
  const fillWidth = layoutGroup.childForceExpandWidth === true || flexibleWidth > 0;
  const fillHeight = layoutGroup.childForceExpandHeight === true || flexibleHeight > 0;

  if (type === 'HorizontalLayoutGroup') {
    setLayoutSizing(figmaNode, 'horizontal', fillWidth ? 'FILL' : 'FIXED');
    setLayoutSizing(figmaNode, 'vertical', fillHeight ? 'FILL' : 'FIXED');
    trySet(figmaNode, 'layoutGrow', fillWidth ? 1 : 0);
    trySet(figmaNode, 'layoutAlign', fillHeight ? 'STRETCH' : 'INHERIT');
  } else if (type === 'VerticalLayoutGroup') {
    setLayoutSizing(figmaNode, 'horizontal', fillWidth ? 'FILL' : 'FIXED');
    setLayoutSizing(figmaNode, 'vertical', fillHeight ? 'FILL' : 'FIXED');
    trySet(figmaNode, 'layoutGrow', fillHeight ? 1 : 0);
    trySet(figmaNode, 'layoutAlign', fillWidth ? 'STRETCH' : 'INHERIT');
  } else if (type === 'GridLayoutGroup') {
    setLayoutSizing(figmaNode, 'horizontal', 'FIXED');
    setLayoutSizing(figmaNode, 'vertical', 'FIXED');
    trySet(figmaNode, 'layoutGrow', 0);
    trySet(figmaNode, 'layoutAlign', 'INHERIT');
  }

  const minWidth = numberOr(layoutElement.minWidth, -1);
  const minHeight = numberOr(layoutElement.minHeight, -1);
  if (minWidth >= 0) {
    trySet(figmaNode, 'minWidth', minWidth);
  }
  if (minHeight >= 0) {
    trySet(figmaNode, 'minHeight', minHeight);
  }

  resizeNode(figmaNode, width, height);
}

function setLayoutSizing(figmaNode, axis, mode) {
  const value = mode === 'FILL' ? 'FILL' : mode === 'HUG' ? 'HUG' : 'FIXED';
  if (axis === 'horizontal') {
    trySet(figmaNode, 'layoutSizingHorizontal', value);
    trySet(figmaNode, 'layoutGrow', value === 'FILL' ? 1 : 0);
  } else {
    trySet(figmaNode, 'layoutSizingVertical', value);
    if (value === 'FILL') {
      trySet(figmaNode, 'layoutAlign', 'STRETCH');
    }
  }
}

function setAbsoluteLayoutPositioning(figmaNode) {
  trySet(figmaNode, 'layoutPositioning', 'ABSOLUTE');
}

function mapFitToSizing(value) {
  const fit = String(value || 'Unconstrained');
  return fit === 'PreferredSize' || fit === 'MinSize' ? 'HUG' : 'FIXED';
}

function mapFitToAutoLayoutSizing(value) {
  return mapFitToSizing(value) === 'HUG' ? 'AUTO' : 'FIXED';
}

function getEnabledLayoutGroup(node) {
  if (!node || !node.layoutGroup || node.layoutGroup.enabled === false) {
    return null;
  }
  if (hasDirectIgnoredLayoutChild(node)) {
    return null;
  }
  return node.layoutGroup;
}

function hasDirectIgnoredLayoutChild(node) {
  const children = Array.isArray(node && node.children) ? node.children : [];
  return children.some(child => {
    const layoutElement = child && child.layoutElement;
    return !!layoutElement &&
      layoutElement.enabled !== false &&
      layoutElement.ignoreLayout === true;
  });
}

function isGridLayoutGroup(layoutGroup) {
  return String(layoutGroup && layoutGroup.componentType || '') === 'GridLayoutGroup';
}

function isManagedByParentLayout(parentNode, node) {
  return !!getEnabledLayoutGroup(parentNode) && !isIgnoredByParentLayout(parentNode, node);
}

function isIgnoredByParentLayout(parentNode, node) {
  return !!getEnabledLayoutGroup(parentNode) &&
    !!node &&
    !!node.layoutElement &&
    node.layoutElement.enabled !== false &&
    node.layoutElement.ignoreLayout === true;
}

function resizeNode(figmaNode, width, height) {
  if (typeof figmaNode.resize !== 'function') {
    return;
  }

  figmaNode.resize(Math.max(1, width), Math.max(1, height));
}

function normalizeUnityScale(value, fallback) {
  const scale = Number(value);
  if (!Number.isFinite(scale)) {
    return fallback;
  }
  if (Math.abs(scale) < 0.0001) {
    return scale < 0 ? -0.0001 : 0.0001;
  }
  return scale;
}

function getUnityScale(rectTransform) {
  const localScale = rectTransform && rectTransform.localScale ? rectTransform.localScale : {};
  return {
    x: normalizeUnityScale(localScale.x, 1),
    y: normalizeUnityScale(localScale.y, 1),
    z: normalizeUnityScale(localScale.z, 1)
  };
}

function getUnityScalePluginData(node) {
  const rt = node && node.rectTransform ? node.rectTransform : null;
  if (!rt || !rt.localScale) {
    return null;
  }

  return {
    x: numberOr(rt.localScale.x, 1),
    y: numberOr(rt.localScale.y, 1),
    z: numberOr(rt.localScale.z, 1)
  };
}

function getScaleContext(scaleContext) {
  return {
    x: numberOr(scaleContext && scaleContext.x, 1),
    y: numberOr(scaleContext && scaleContext.y, 1),
    z: numberOr(scaleContext && scaleContext.z, 1)
  };
}

function getNodeVisualState(node, unitySize, parentScaleContext, applySelfScale) {
  const parentScale = getScaleContext(parentScaleContext);
  const localScale = applySelfScale ? getUnityScale(node.rectTransform || {}) : { x: 1, y: 1, z: 1 };
  const scaleContext = {
    x: parentScale.x * localScale.x,
    y: parentScale.y * localScale.y,
    z: parentScale.z * localScale.z
  };
  return {
    width: Math.max(1, numberOr(unitySize && unitySize.width, 1) * Math.abs(scaleContext.x)),
    height: Math.max(1, numberOr(unitySize && unitySize.height, 1) * Math.abs(scaleContext.y)),
    unityWidth: numberOr(unitySize && unitySize.width, 1),
    unityHeight: numberOr(unitySize && unitySize.height, 1),
    parentScale: parentScale,
    localScale: localScale,
    scaleContext: scaleContext
  };
}

function trySet(target, key, value) {
  try {
    target[key] = value;
  } catch (error) {
    // Some Figma editor versions do not expose newer auto-layout fields.
  }
}

function getNodeSize(node, parentSize) {
  const rt = node.rectTransform || {};
  const rect = rt.rect || {};
  const sizeDelta = rt.sizeDelta || {};
  const anchorMin = rt.anchorMin || { x: 0.5, y: 0.5 };
  const anchorMax = rt.anchorMax || { x: 0.5, y: 0.5 };
  let width = positiveNumber(rect.width) || positiveNumber(sizeDelta.x) || 1;
  let height = positiveNumber(rect.height) || positiveNumber(sizeDelta.y) || 1;

  if (parentSize) {
    const anchorWidth = Math.abs(numberOr(anchorMax.x, 0.5) - numberOr(anchorMin.x, 0.5));
    const anchorHeight = Math.abs(numberOr(anchorMax.y, 0.5) - numberOr(anchorMin.y, 0.5));
    if (anchorWidth > 0.0001) {
      width = Math.max(1, parentSize.width * anchorWidth + numberOr(sizeDelta.x, 0));
    }
    if (anchorHeight > 0.0001) {
      height = Math.max(1, parentSize.height * anchorHeight + numberOr(sizeDelta.y, 0));
    }
  }

  return { width, height };
}

function getNodePosition(node, parentWidth, parentHeight, width, height) {
  const rt = node.rectTransform || {};
  const anchorMin = rt.anchorMin || { x: 0.5, y: 0.5 };
  const anchorMax = rt.anchorMax || { x: 0.5, y: 0.5 };
  const anchored = rt.anchoredPosition || { x: 0, y: 0 };
  const pivot = rt.pivot || { x: 0.5, y: 0.5 };
  const pivotX = numberOr(pivot.x, 0.5);
  const pivotY = numberOr(pivot.y, 0.5);
  const anchorX = parentWidth * lerp(numberOr(anchorMin.x, 0.5), numberOr(anchorMax.x, 0.5), pivotX);
  const anchorYFromBottom = parentHeight * lerp(numberOr(anchorMin.y, 0.5), numberOr(anchorMax.y, 0.5), pivotY);

  return {
    x: anchorX + numberOr(anchored.x, 0) - width * pivotX,
    y: parentHeight - anchorYFromBottom - numberOr(anchored.y, 0) - height * (1 - pivotY)
  };
}

function applyUnityTransform(figmaNode, node, parentUnitySize, parentScaleContext, unitySize, visualState) {
  const rt = node.rectTransform || {};
  const pivot = rt.pivot || { x: 0.5, y: 0.5 };
  const pivotX = numberOr(pivot.x, 0.5);
  const pivotY = numberOr(pivot.y, 0.5);
  const parentWidth = numberOr(parentUnitySize && parentUnitySize.width, 1);
  const parentHeight = numberOr(parentUnitySize && parentUnitySize.height, 1);
  const parentScale = getScaleContext(parentScaleContext);
  const pivotPosition = getUnityPivotPosition(node, parentWidth, parentHeight);
  const pivotVisual = {
    x: pivotPosition.x * Math.abs(parentScale.x),
    y: pivotPosition.y * Math.abs(parentScale.y)
  };
  const localScale = getUnityScale(rt);
  const signX = localScale.x < 0 ? -1 : 1;
  const signY = localScale.y < 0 ? -1 : 1;
  const radians = -normalizeRotation(rt.localEulerAngles ? rt.localEulerAngles.z || 0 : 0) * Math.PI / 180;
  const cos = Math.cos(radians);
  const sin = Math.sin(radians);
  const a = cos * signX;
  const b = sin * signX;
  const c = -sin * signY;
  const d = cos * signY;
  const visualWidth = numberOr(visualState && visualState.width, unitySize && unitySize.width || 1);
  const visualHeight = numberOr(visualState && visualState.height, unitySize && unitySize.height || 1);
  const localPivotX = visualWidth * pivotX;
  const localPivotY = visualHeight * (1 - pivotY);
  const tx = pivotVisual.x - (a * localPivotX + c * localPivotY);
  const ty = pivotVisual.y - (b * localPivotX + d * localPivotY);

  try {
    figmaNode.relativeTransform = [[a, c, tx], [b, d, ty]];
  } catch (error) {
    figmaNode.x = tx;
    figmaNode.y = ty;
    figmaNode.rotation = -normalizeRotation(rt.localEulerAngles ? rt.localEulerAngles.z || 0 : 0);
  }
}

function getUnityPivotPosition(node, parentWidth, parentHeight) {
  const rt = node.rectTransform || {};
  const anchorMin = rt.anchorMin || { x: 0.5, y: 0.5 };
  const anchorMax = rt.anchorMax || { x: 0.5, y: 0.5 };
  const anchored = rt.anchoredPosition || { x: 0, y: 0 };
  const pivot = rt.pivot || { x: 0.5, y: 0.5 };
  const pivotX = numberOr(pivot.x, 0.5);
  const pivotY = numberOr(pivot.y, 0.5);
  const anchorX = parentWidth * lerp(numberOr(anchorMin.x, 0.5), numberOr(anchorMax.x, 0.5), pivotX);
  const anchorYFromBottom = parentHeight * lerp(numberOr(anchorMin.y, 0.5), numberOr(anchorMax.y, 0.5), pivotY);
  return {
    x: anchorX + numberOr(anchored.x, 0),
    y: parentHeight - anchorYFromBottom - numberOr(anchored.y, 0)
  };
}

function applyConstraints(frame, node) {
  const rt = node.rectTransform || {};
  const anchorMin = rt.anchorMin || { x: 0.5, y: 0.5 };
  const anchorMax = rt.anchorMax || { x: 0.5, y: 0.5 };
  frame.constraints = {
    horizontal: mapHorizontalConstraint(numberOr(anchorMin.x, 0.5), numberOr(anchorMax.x, 0.5)),
    vertical: mapVerticalConstraint(numberOr(anchorMin.y, 0.5), numberOr(anchorMax.y, 0.5))
  };
}

function mapHorizontalConstraint(min, max) {
  if (nearly(min, 0) && nearly(max, 1)) return 'STRETCH';
  if (!nearly(min, max)) return 'SCALE';
  if (nearly(min, 0)) return 'MIN';
  if (nearly(min, 1)) return 'MAX';
  return 'CENTER';
}

function mapVerticalConstraint(min, max) {
  if (nearly(min, 0) && nearly(max, 1)) return 'STRETCH';
  if (!nearly(min, max)) return 'SCALE';
  if (nearly(min, 1)) return 'MIN';
  if (nearly(min, 0)) return 'MAX';
  return 'CENTER';
}

function setBridgeNodePluginData(figmaNode, node, fillMetadata) {
  const graphics = fillMetadata && fillMetadata.graphics ? fillMetadata.graphics : [];
  const unityScale = getUnityScalePluginData(node);
  const nodeData = {
    path: node.path || '',
    localId: node.localId || 0,
    activeSelf: node.activeSelf !== false,
    unityScale: unityScale,
    rectTransform: node.rectTransform || null,
    prefabReference: node.prefabReference || null,
    layoutGroup: node.layoutGroup || null,
    layoutElement: node.layoutElement || null,
    contentSizeFitter: node.contentSizeFitter || null
  };

  if (graphics.length > 0) {
    nodeData.graphics = graphics;
  }

  setBridgePluginData(figmaNode, 'figmaBridgeNode', nodeData);

  if (unityScale) {
    setBridgePluginData(figmaNode, 'figmaBridgeUnityScale', unityScale);
  } else {
    clearBridgePluginData(figmaNode, 'figmaBridgeUnityScale');
  }

  if (graphics.length > 0) {
    setBridgePluginData(figmaNode, 'figmaBridgeGraphics', graphics);
  } else {
    clearBridgePluginData(figmaNode, 'figmaBridgeGraphics');
  }

  if (fillMetadata && fillMetadata.imageSource) {
    setBridgePluginData(figmaNode, 'figmaBridgeImageSource', fillMetadata.imageSource);
  } else {
    clearBridgePluginData(figmaNode, 'figmaBridgeImageSource');
  }

  if (node.layoutGroup) {
    setBridgePluginData(figmaNode, 'figmaBridgeLayoutGroup', node.layoutGroup);
  } else {
    clearBridgePluginData(figmaNode, 'figmaBridgeLayoutGroup');
  }

  if (node.layoutElement) {
    setBridgePluginData(figmaNode, 'figmaBridgeLayoutElement', node.layoutElement);
  } else {
    clearBridgePluginData(figmaNode, 'figmaBridgeLayoutElement');
  }

  if (node.contentSizeFitter) {
    setBridgePluginData(figmaNode, 'figmaBridgeContentSizeFitter', node.contentSizeFitter);
  } else {
    clearBridgePluginData(figmaNode, 'figmaBridgeContentSizeFitter');
  }
}

function applyFills(frame, node, imageMap, size) {
  const graphics = (node.graphics || []).filter(graphic => {
    const type = String(graphic.componentType || '').toLowerCase();
    return graphic.enabled !== false &&
      type.indexOf('text') < 0 &&
      type.indexOf('tmp') < 0 &&
      type.indexOf('raycast') < 0;
  });
  const metadata = buildGraphicsPluginData(graphics);

  if (graphics.length === 0) {
    frame.fills = [];
    return metadata;
  }

  const graphic = graphics[graphics.length - 1];
  const color = parseColor(graphic.color);
  const sprite = graphic.sprite;

  if (sprite && sprite.imageFile && imageMap[sprite.imageFile]) {
    const imageHash = resolveImageHash(imageMap[sprite.imageFile]);
    frame.fills = [{
      type: 'IMAGE',
      scaleMode: 'FILL',
      imageHash: imageHash,
      opacity: color.a
    }];
    metadata.imageSource = buildImageSourcePluginData(graphic, sprite.imageFile, imageHash);
    markImportedImage(metadata.graphics, graphic, sprite.imageFile, imageHash);
    return metadata;
  }

  if (color.a > 0) {
    frame.fills = [{
      type: 'SOLID',
      color: { r: color.r, g: color.g, b: color.b },
      opacity: color.a
    }];
  } else {
    frame.fills = [];
  }
  return metadata;
}

function buildGraphicsPluginData(graphics) {
  const output = [];
  for (let i = 0; i < graphics.length; i++) {
    const graphic = graphics[i];
    const sprite = graphic.sprite ? normalizeSpriteSource(graphic.sprite) : null;
    if (!sprite && !graphic.materialGuid && !graphic.materialPath) {
      continue;
    }

    output.push({
      index: i,
      componentType: graphic.componentType || '',
      enabled: graphic.enabled !== false,
      color: graphic.color || '',
      materialGuid: graphic.materialGuid || '',
      materialLocalId: graphic.materialLocalId || 0,
      materialPath: graphic.materialPath || '',
      imageType: graphic.imageType || '',
      preserveAspect: graphic.preserveAspect === true,
      fillMethod: graphic.fillMethod || '',
      fillAmount: numberOr(graphic.fillAmount, 1),
      sprite: sprite,
      nineSlice: normalizeNineSliceSource(graphic.nineSlice)
    });
  }
  return { graphics: output };
}

function markImportedImage(graphicsMetadata, sourceGraphic, imageFile, imageHash) {
  if (!graphicsMetadata || graphicsMetadata.length === 0) {
    return;
  }

  for (let i = graphicsMetadata.length - 1; i >= 0; i--) {
    const item = graphicsMetadata[i];
    if (item.sprite && sourceGraphic.sprite &&
      item.sprite.guid === (sourceGraphic.sprite.guid || '') &&
      item.sprite.localId === (sourceGraphic.sprite.localId || 0)) {
      item.importedImage = {
        imageFile: imageFile || '',
        imageHash: imageHash || ''
      };
      return;
    }
  }
}

function buildImageSourcePluginData(graphic, imageFile, imageHash, pieceName) {
  return {
    componentType: graphic.componentType || '',
    imageType: graphic.imageType || '',
    preserveAspect: graphic.preserveAspect === true,
    fillMethod: graphic.fillMethod || '',
    fillAmount: numberOr(graphic.fillAmount, 1),
    imageFile: imageFile || '',
    importedImageHash: imageHash || '',
    pieceName: pieceName || '',
    sprite: graphic.sprite ? normalizeSpriteSource(graphic.sprite) : null,
    nineSlice: normalizeNineSliceSource(graphic.nineSlice)
  };
}

function normalizeSpriteSource(sprite) {
  if (!sprite) {
    return null;
  }

  return {
    name: sprite.name || '',
    imageFile: sprite.imageFile || '',
    assetPath: sprite.assetPath || '',
    guid: sprite.guid || '',
    localId: sprite.localId || 0,
    rect: sprite.rect || null,
    border: sprite.border || null,
    pixelsPerUnit: numberOr(sprite.pixelsPerUnit, 100)
  };
}

function normalizeNineSliceSource(nineSlice) {
  if (!nineSlice || nineSlice.enabled === false) {
    return null;
  }

  return {
    enabled: nineSlice.enabled !== false,
    left: numberOr(nineSlice.left, 0),
    right: numberOr(nineSlice.right, 0),
    top: numberOr(nineSlice.top, 0),
    bottom: numberOr(nineSlice.bottom, 0),
    sourceWidth: numberOr(nineSlice.sourceWidth, 0),
    sourceHeight: numberOr(nineSlice.sourceHeight, 0)
  };
}

function createNineSliceLayers(parent, nineSlice, imageMap, size, sourceGraphic) {
  const width = size.width;
  const height = size.height;
  const left = Math.min(numberOr(nineSlice.left, 0), width / 2);
  const right = Math.min(numberOr(nineSlice.right, 0), Math.max(0, width - left));
  const top = Math.min(numberOr(nineSlice.top, 0), height / 2);
  const bottom = Math.min(numberOr(nineSlice.bottom, 0), Math.max(0, height - top));
  const centerWidth = Math.max(0, width - left - right);
  const centerHeight = Math.max(0, height - top - bottom);

  const pieces = [
    ['topLeft', nineSlice.topLeftImage, 0, 0, left, top],
    ['top', nineSlice.topImage, left, 0, centerWidth, top],
    ['topRight', nineSlice.topRightImage, width - right, 0, right, top],
    ['left', nineSlice.leftImage, 0, top, left, centerHeight],
    ['center', nineSlice.centerImage, left, top, centerWidth, centerHeight],
    ['right', nineSlice.rightImage, width - right, top, right, centerHeight],
    ['bottomLeft', nineSlice.bottomLeftImage, 0, height - bottom, left, bottom],
    ['bottom', nineSlice.bottomImage, left, height - bottom, centerWidth, bottom],
    ['bottomRight', nineSlice.bottomRightImage, width - right, height - bottom, right, bottom]
  ];

  for (const piece of pieces) {
    const name = piece[0];
    const imageFile = piece[1];
    const x = piece[2];
    const y = piece[3];
    const w = piece[4];
    const h = piece[5];
    if (!imageFile || !imageMap[imageFile] || w <= 0 || h <= 0) {
      continue;
    }

    const imageHash = resolveImageHash(imageMap[imageFile]);
    const rect = figma.createRectangle();
    rect.name = `_9_${name}`;
    parent.appendChild(rect);
    rect.resize(Math.max(1, w), Math.max(1, h));
    rect.x = x;
    rect.y = y;
    rect.fills = [{
      type: 'IMAGE',
      scaleMode: 'FILL',
      imageHash: imageHash
    }];
    if (sourceGraphic) {
      setBridgePluginData(rect, 'figmaBridgeImageSource', buildImageSourcePluginData(sourceGraphic, imageFile, imageHash, name));
    }
  }
}

function resolveImageHash(imagePayload) {
  if (typeof imagePayload === 'string') {
    return imagePayload;
  }

  const image = figma.createImage(Uint8Array.from(imagePayload));
  return image.hash;
}

async function createTextLayer(parent, textInfo, width, height) {
  const fontName = await loadFontForText(textInfo);
  const text = figma.createText();
  text.name = `${parent.name}_text`;
  text.visible = textInfo.enabled !== false;
  text.fontName = fontName;
  text.characters = textInfo.text || '';
  text.fontSize = mapUnityFontSizeToFigma(textInfo.fontSize);
  text.textAutoResize = 'NONE';
  text.resize(Math.max(1, width), Math.max(1, height));
  parent.appendChild(text);
  text.x = 0;
  text.y = 0;

  const color = parseColor(textInfo.color);
  text.fills = [{
    type: 'SOLID',
    color: { r: color.r, g: color.g, b: color.b },
    opacity: color.a
  }];

  applyTextAlignment(text, textInfo.alignment);
  setBridgePluginData(text, 'figmaBridgeText', {
    componentType: textInfo.componentType || '',
    enabled: textInfo.enabled !== false,
    text: textInfo.text || '',
    color: textInfo.color || '',
    fontSize: textInfo.fontSize || 0,
    figmaFontSize: mapUnityFontSizeToFigma(textInfo.fontSize),
    fontStyle: textInfo.fontStyle || '',
    alignment: textInfo.alignment || '',
    fontName: textInfo.fontName || '',
    figmaFontName: textInfo.figmaFontName || '',
    fontPath: textInfo.fontPath || '',
    fontGuid: textInfo.fontGuid || '',
    fontLocalId: textInfo.fontLocalId || 0,
    raycastTarget: textInfo.raycastTarget === true
  });
}

async function buildUnityPasteClipboardPayload() {
  const selection = figma.currentPage.selection || [];
  if (selection.length === 0) {
    throw new Error('Select one or more Figma nodes first.');
  }

  const selectionBounds = calculateUnityPasteSelectionBounds(selection);
  const nodes = [];
  for (const selectedNode of selection) {
    const pasteNode = await buildUnityPasteNodeTree(selectedNode, selectionBounds, true);
    if (pasteNode) {
      nodes.push(pasteNode);
    }
  }

  if (nodes.length === 0) {
    throw new Error('Selection has no supported UI node or Unity source reference.');
  }

  return {
    schema: UNITY_PASTE_SCHEMA,
    version: 2,
    exporter: 'Unity Figma Bridge Importer',
    exportedAt: new Date().toISOString(),
    selection: {
      x: roundForPaste(selectionBounds.x),
      y: roundForPaste(selectionBounds.y),
      width: roundForPaste(selectionBounds.width),
      height: roundForPaste(selectionBounds.height)
    },
    nodes
  };
}

async function buildUnityPasteNodeTree(node, parentBounds, isSelectionRoot) {
  if (!node || node.visible === false) {
    return null;
  }

  const bounds = getUnityPasteNodeBounds(node);
  if (!bounds) {
    return null;
  }

  const size = getUnityPasteNodeSize(node, bounds);
  const source = await getUnityPasteSource(node);
  const visual = source ? null : await tryBuildUnityPasteVisual(node, bounds);
  const children = [];
  if (!source && 'children' in node) {
    for (const child of node.children) {
      const childNode = await buildUnityPasteNodeTree(child, bounds, false);
      if (childNode) {
        children.push(childNode);
      }
    }
  }

  if (!source && !visual && children.length === 0) {
    return null;
  }

  let x;
  let y;
  if (isSelectionRoot) {
    x = bounds.x - parentBounds.x + (bounds.width - size.width) * 0.5;
    y = bounds.y - parentBounds.y + (bounds.height - size.height) * 0.5;
  } else {
    x = numberOr(node.x, bounds.x - parentBounds.x);
    y = numberOr(node.y, bounds.y - parentBounds.y);
  }

  const scale = getUnityPasteScale(node);
  const pasteNode = {
    kind: source ? 'reference' : (visual ? visual.kind : 'group'),
    name: node.name || (source ? 'unity_reference' : 'group'),
    x: roundForPaste(x),
    y: roundForPaste(y),
    width: roundForPaste(size.width),
    height: roundForPaste(size.height),
    rotation: roundForPaste(normalizeRotation(numberOr(node.rotation, 0))),
    scaleX: scale.x,
    scaleY: scale.y
  };

  if (source) {
    pasteNode.source = source;
  }
  if (visual) {
    Object.assign(pasteNode, visual);
    pasteNode.kind = visual.kind;
  }
  if (children.length > 0) {
    pasteNode.children = children;
  }

  return pasteNode;
}

async function tryBuildUnityPasteVisual(node, bounds) {
  if (node.type === 'TEXT') {
    const textFill = findFirstVisibleSolidPaint(node.fills);
    const textMetadata = readPluginJson(node, 'figmaBridgeText') || {};
    const fontName = node.fontName && typeof node.fontName === 'object' ? node.fontName : {};
    return {
      kind: 'text',
      fill: textFill ? colorFromSolidPaint(textFill) : { r: 1, g: 1, b: 1, a: 1 },
      characters: node.characters || '',
      fontSize: typeof node.fontSize === 'number' ? roundForPaste(node.fontSize) : 32,
      fontFamily: textMetadata.figmaFontName || fontName.family || '',
      fontStyle: fontName.style || textMetadata.fontStyle || '',
      unityFontName: textMetadata.fontName || '',
      fontPath: textMetadata.fontPath || '',
      fontGuid: textMetadata.fontGuid || '',
      textAlignHorizontal: node.textAlignHorizontal || '',
      textAlignVertical: node.textAlignVertical || ''
    };
  }

  const imagePaint = findFirstVisibleImagePaint(node.fills);
  if (imagePaint) {
    return await buildUnityPasteImageVisual(node, bounds);
  }

  const solidPaint = findFirstVisibleSolidPaint(node.fills);
  if (solidPaint && (node.type === 'RECTANGLE' || node.type === 'FRAME')) {
    return {
      kind: 'rectangle',
      cornerRadius: typeof node.cornerRadius === 'number' ? roundForPaste(node.cornerRadius) : 0,
      fill: colorFromSolidPaint(solidPaint)
    };
  }

  if (solidPaint || findFirstVisiblePaint(node.strokes)) {
    return await buildUnityPasteImageVisual(node, bounds);
  }

  return null;
}

async function buildUnityPasteImageVisual(node, bounds) {
  const imageBytes = await exportUnityPasteVisualPng(node);
  return {
    kind: 'image',
    image: {
      mimeType: 'image/png',
      base64: byteArrayToBase64(imageBytes),
      width: Math.max(1, Math.round(bounds.width)),
      height: Math.max(1, Math.round(bounds.height))
    }
  };
}

async function exportUnityPasteVisualPng(node) {
  let exportNode = node;
  let clone = null;
  if ('children' in node && node.children.length > 0 && typeof node.clone === 'function') {
    clone = node.clone();
    clone.visible = true;
    if ('children' in clone) {
      for (const child of clone.children.slice()) {
        child.remove();
      }
    }
    exportNode = clone;
  }

  try {
    return await exportNode.exportAsync({
      format: 'PNG',
      constraint: { type: 'SCALE', value: 1 }
    });
  } finally {
    if (clone) {
      clone.remove();
    }
  }
}

async function getUnityPasteSource(node) {
  const candidates = [node];
  const mainComponent = await getUnityPasteMainComponent(node);
  if (mainComponent) {
    candidates.push(mainComponent);
  }

  for (const candidate of candidates) {
    const rootSource = readPluginJson(candidate, 'figmaBridgeSource') || null;
    const bridgeNode = readPluginJson(candidate, 'figmaBridgeNode') || {};
    const prefabReference = bridgeNode.prefabReference || null;
    const source = rootSource || (prefabReference && prefabReference.isPrefabInstanceRoot
      ? {
          prefabName: prefabReference.sourcePrefabName || '',
          prefabPath: prefabReference.sourcePrefabPath || '',
          prefabGuid: prefabReference.sourcePrefabGuid || '',
          sourceLocalId: prefabReference.sourceLocalId || 0,
          instanceRootPath: prefabReference.instanceRootPath || ''
        }
      : null);
    if (source && (source.prefabPath || source.prefabGuid)) {
      return {
        prefabName: getUnityPastePrefabName(source),
        prefabPath: source.prefabPath || '',
        prefabGuid: source.prefabGuid || '',
        sourceLocalId: source.sourceLocalId || 0,
        instanceRootPath: source.instanceRootPath || '',
        componentName: candidate.name || node.name || '',
        variantProperties: getUnityPasteVariantProperties(node, candidate),
        nodeStates: collectUnityPasteNodeStates(node, candidate)
      };
    }
  }

  return null;
}

function getUnityPastePrefabName(source) {
  const explicitName = String(source && (source.prefabName || source.sourcePrefabName) || '').trim();
  if (explicitName) {
    return explicitName;
  }

  const path = String(source && (source.prefabPath || source.sourcePrefabPath) || '').replace(/\\/g, '/');
  const fileName = path.split('/').pop() || '';
  return fileName.replace(/\.prefab$/i, '');
}

function collectUnityPasteNodeStates(actualRoot, templateRoot) {
  const states = [];
  const seenPaths = {};
  collectUnityPasteNodeStatesRecursive(actualRoot, templateRoot, states, seenPaths);
  return states;
}

function collectUnityPasteNodeStatesRecursive(actualNode, templateNode, output, seenPaths) {
  if (!actualNode && !templateNode) {
    return;
  }

  const bridgeNode = readPluginJson(actualNode, 'figmaBridgeNode') ||
    readPluginJson(templateNode, 'figmaBridgeNode') || null;
  const path = bridgeNode ? String(bridgeNode.path || '') : '';
  if (path && !seenPaths[path]) {
    seenPaths[path] = true;
    output.push({
      path,
      active: actualNode ? actualNode.visible !== false : templateNode.visible !== false
    });
  }

  const actualChildren = actualNode && 'children' in actualNode ? actualNode.children : [];
  const templateChildren = templateNode && 'children' in templateNode ? templateNode.children : [];
  for (let i = 0; i < actualChildren.length; i += 1) {
    const actualChild = actualChildren[i];
    const templateChild = findUnityPasteTemplateChild(actualChild, templateChildren, i);
    collectUnityPasteNodeStatesRecursive(actualChild, templateChild, output, seenPaths);
  }
}

function findUnityPasteTemplateChild(actualChild, templateChildren, fallbackIndex) {
  if (!actualChild || !Array.isArray(templateChildren)) {
    return null;
  }

  const nameMatch = templateChildren.find(child =>
    child && child.name === actualChild.name && child.type === actualChild.type);
  return nameMatch || templateChildren[fallbackIndex] || null;
}

async function getUnityPasteMainComponent(node) {
  if (!node || node.type !== 'INSTANCE') {
    return null;
  }

  if (typeof node.getMainComponentAsync === 'function') {
    try {
      return await node.getMainComponentAsync();
    } catch (error) {
      return null;
    }
  }

  try {
    return node.mainComponent || null;
  } catch (error) {
    return null;
  }
}

function getUnityPasteVariantProperties(node, component) {
  const raw = (node && node.componentProperties) || (component && component.variantProperties) || null;
  if (!raw) {
    return '';
  }

  const values = {};
  for (const key of Object.keys(raw)) {
    const entry = raw[key];
    values[key] = entry && typeof entry === 'object' && 'value' in entry ? entry.value : entry;
  }
  return JSON.stringify(values);
}

function getUnityPasteNodeSize(node, bounds) {
  return {
    width: positiveNumber(node && node.width) || bounds.width,
    height: positiveNumber(node && node.height) || bounds.height
  };
}

function getUnityPasteScale(node) {
  const transform = node && node.relativeTransform;
  if (!Array.isArray(transform) || !Array.isArray(transform[0]) || !Array.isArray(transform[1])) {
    return { x: 1, y: 1 };
  }

  const a = numberOr(transform[0][0], 1);
  const c = numberOr(transform[0][1], 0);
  const b = numberOr(transform[1][0], 0);
  const d = numberOr(transform[1][1], 1);
  return { x: a * d - b * c < 0 ? -1 : 1, y: 1 };
}

function getUnityPasteNodeBounds(node) {
  const absolute = node.absoluteBoundingBox || null;
  const width = positiveNumber(absolute && absolute.width) || positiveNumber(node.width);
  const height = positiveNumber(absolute && absolute.height) || positiveNumber(node.height);
  if (!width || !height) {
    return null;
  }

  return {
    x: numberOr(absolute && absolute.x, numberOr(node.x, 0)),
    y: numberOr(absolute && absolute.y, numberOr(node.y, 0)),
    width,
    height
  };
}

function findFirstVisibleImagePaint(fills) {
  if (!Array.isArray(fills)) {
    return null;
  }

  for (const fill of fills) {
    if (fill && fill.visible !== false && fill.type === 'IMAGE') {
      return fill;
    }
  }

  return null;
}

function findFirstVisibleSolidPaint(fills) {
  if (!Array.isArray(fills)) {
    return null;
  }

  for (const fill of fills) {
    if (!fill || fill.visible === false || fill.type !== 'SOLID') {
      continue;
    }

    const opacity = numberOr(fill.opacity, 1);
    if (opacity > 0) {
      return fill;
    }
  }

  return null;
}

function findFirstVisiblePaint(paints) {
  if (!Array.isArray(paints)) {
    return null;
  }

  for (const paint of paints) {
    if (paint && paint.visible !== false && numberOr(paint.opacity, 1) > 0) {
      return paint;
    }
  }

  return null;
}

function colorFromSolidPaint(paint) {
  const color = paint && paint.color ? paint.color : {};
  return {
    r: clampForPaste(numberOr(color.r, 0)),
    g: clampForPaste(numberOr(color.g, 0)),
    b: clampForPaste(numberOr(color.b, 0)),
    a: clampForPaste(numberOr(paint && paint.opacity, 1))
  };
}

function calculateUnityPasteSelectionBounds(nodes) {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const node of nodes) {
    const bounds = getUnityPasteNodeBounds(node);
    if (!bounds) {
      continue;
    }
    minX = Math.min(minX, bounds.x);
    minY = Math.min(minY, bounds.y);
    maxX = Math.max(maxX, bounds.x + bounds.width);
    maxY = Math.max(maxY, bounds.y + bounds.height);
  }

  if (!Number.isFinite(minX) || !Number.isFinite(minY) ||
      !Number.isFinite(maxX) || !Number.isFinite(maxY)) {
    throw new Error('The selected Figma nodes do not have measurable bounds.');
  }

  return {
    x: minX,
    y: minY,
    width: Math.max(1, maxX - minX),
    height: Math.max(1, maxY - minY)
  };
}

function formatUnityPasteClipboardText(payload) {
  return `${UNITY_PASTE_MARKER}\n${JSON.stringify(payload)}`;
}

function buildUnityPasteSummary(payload) {
  const stats = countUnityPasteNodes(payload && payload.nodes);
  const referenceText = stats.references > 0 ? `, ${stats.references} Unity reference${stats.references === 1 ? '' : 's'}` : '';
  return `Copied ${stats.total} Figma node${stats.total === 1 ? '' : 's'}${referenceText} for Unity Scene paste.`;
}

function countUnityPasteNodes(nodes) {
  const stats = { total: 0, references: 0 };
  if (!Array.isArray(nodes)) {
    return stats;
  }

  for (const node of nodes) {
    if (!node) {
      continue;
    }
    stats.total += 1;
    if (node.kind === 'reference') {
      stats.references += 1;
    }
    const childStats = countUnityPasteNodes(node.children);
    stats.total += childStats.total;
    stats.references += childStats.references;
  }
  return stats;
}

function byteArrayToBase64(bytes) {
  const table = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  let output = '';
  for (let i = 0; i < bytes.length; i += 3) {
    const first = bytes[i];
    const second = i + 1 < bytes.length ? bytes[i + 1] : 0;
    const third = i + 2 < bytes.length ? bytes[i + 2] : 0;
    const triplet = (first << 16) | (second << 8) | third;
    output += table[(triplet >> 18) & 63];
    output += table[(triplet >> 12) & 63];
    output += i + 1 < bytes.length ? table[(triplet >> 6) & 63] : '=';
    output += i + 2 < bytes.length ? table[triplet & 63] : '=';
  }
  return output;
}

function roundForPaste(value) {
  const number = numberOr(value, 0);
  return Math.round(number * 1000) / 1000;
}

function clampForPaste(value) {
  return Math.max(0, Math.min(1, numberOr(value, 0)));
}

function exportSelectionForUnity() {
  const selection = figma.currentPage.selection || [];
  if (selection.length !== 1) {
    throw new Error('Select exactly one imported Figma Bridge root node before exporting for Unity.');
  }

  return exportRootForUnity(selection[0]);
}

function exportRootForUnity(root) {
  const source = readPluginJson(root, 'figmaBridgeSource') || {};
  if (!source.prefabPath && !source.prefabGuid) {
    throw new Error('The node does not have figmaBridgeSource plugin data. Select or import a Figma Bridge frame/component root.');
  }

  return {
    schemaVersion: '1.0',
    exporter: 'Unity Figma Bridge Figma Plugin',
    exportedAt: new Date().toISOString(),
    source: source,
    root: exportFigmaNode(root)
  };
}

function exportFigmaNode(node) {
  const bridgeNode = readPluginJson(node, 'figmaBridgeNode') || {};
  const unityScale = readPluginJson(node, 'figmaBridgeUnityScale') || bridgeNode.unityScale || null;
  const graphics = readPluginJson(node, 'figmaBridgeGraphics') || bridgeNode.graphics || [];
  const imageSource = readPluginJson(node, 'figmaBridgeImageSource') || null;
  const layoutGroup = readPluginJson(node, 'figmaBridgeLayoutGroup') || bridgeNode.layoutGroup || null;
  const layoutElement = readPluginJson(node, 'figmaBridgeLayoutElement') || bridgeNode.layoutElement || null;
  const contentSizeFitter = readPluginJson(node, 'figmaBridgeContentSizeFitter') || bridgeNode.contentSizeFitter || null;
  const children = [];
  const textLayers = [];

  if ('children' in node) {
    for (const child of node.children) {
      if (child.type === 'TEXT') {
        textLayers.push(exportTextLayer(child));
      } else if (readPluginJson(child, 'figmaBridgeNode')) {
        children.push(exportFigmaNode(child));
      }
    }
  }

  return {
    name: node.name || '',
    type: node.type || '',
    visible: node.visible !== false,
    width: numberOr(node.width, 0),
    height: numberOr(node.height, 0),
    x: numberOr(node.x, 0),
    y: numberOr(node.y, 0),
    rotation: numberOr(node.rotation, 0),
    currentImageHash: getPrimaryImageHash(node),
    bridgeNode: bridgeNode,
    unityScale: unityScale,
    graphics: graphics,
    imageSource: imageSource,
    layoutGroup: layoutGroup,
    layoutElement: layoutElement,
    contentSizeFitter: contentSizeFitter,
    textLayers: textLayers,
    children: children
  };
}

function exportTextLayer(textNode) {
  const source = readPluginJson(textNode, 'figmaBridgeText') || {};
  const fill = Array.isArray(textNode.fills) && textNode.fills.length > 0 ? textNode.fills[0] : null;
  return {
    name: textNode.name || '',
    visible: textNode.visible !== false,
    characters: textNode.characters || '',
    fontSize: numberOr(textNode.fontSize, 0),
    source: source,
    color: fill && fill.type === 'SOLID' ? {
      r: numberOr(fill.color && fill.color.r, 0),
      g: numberOr(fill.color && fill.color.g, 0),
      b: numberOr(fill.color && fill.color.b, 0),
      a: numberOr(fill.opacity, 1)
    } : null
  };
}

function getPrimaryImageHash(node) {
  if (!Array.isArray(node.fills)) {
    return '';
  }

  for (const fill of node.fills) {
    if (fill && fill.type === 'IMAGE' && fill.imageHash) {
      return fill.imageHash;
    }
  }
  return '';
}

function normalizeImportPath(path) {
  return String(path || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, '');
}

function readPluginJson(node, key) {
  if (!node) {
    return null;
  }

  let raw = '';
  if (typeof node.getSharedPluginData === 'function') {
    raw = node.getSharedPluginData(BRIDGE_NAMESPACE, key);
  }

  if (!raw && typeof node.getPluginData === 'function') {
    raw = node.getPluginData(key);
  }

  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw);
  } catch (error) {
    return null;
  }
}

function setBridgePluginData(node, key, value) {
  if (!node) {
    return;
  }

  const serialized = JSON.stringify(value);
  if (typeof node.setSharedPluginData === 'function') {
    node.setSharedPluginData(BRIDGE_NAMESPACE, key, serialized);
    return;
  }

  if (typeof node.setPluginData === 'function') {
    node.setPluginData(key, serialized);
  }
}

function clearBridgePluginData(node, key) {
  if (!node) {
    return;
  }

  if (typeof node.setSharedPluginData === 'function') {
    node.setSharedPluginData(BRIDGE_NAMESPACE, key, '');
  }

  if (typeof node.setPluginData === 'function') {
    node.setPluginData(key, '');
  }
}

async function loadFontForText(textInfo) {
  const family = textInfo.figmaFontName || mapUnityFontToFigma(textInfo.fontName) || DEFAULT_FONT.family;
  const requested = await resolveAvailableFont(family);
  const key = `${requested.family}///${requested.style}`;
  if (loadedFonts[key]) {
    return requested;
  }

  try {
    await figma.loadFontAsync(requested);
    loadedFonts[key] = true;
    return requested;
  } catch (error) {
    const fallbackKey = `${DEFAULT_FONT.family}///${DEFAULT_FONT.style}`;
    if (!loadedFonts[fallbackKey]) {
      await figma.loadFontAsync(DEFAULT_FONT);
      loadedFonts[fallbackKey] = true;
    }
    return DEFAULT_FONT;
  }
}

async function resolveAvailableFont(family) {
  let fonts = [];
  try {
    fonts = await getAvailableFonts();
  } catch (error) {
    return { family, style: 'Regular' };
  }

  const exact = fonts.filter(font => font.fontName.family === family);
  if (exact.length > 0) {
    const regular = exact.find(font => /regular|normal/i.test(font.fontName.style));
    return (regular || exact[0]).fontName;
  }

  const fallback = fonts.find(font => font.fontName.family === DEFAULT_FONT.family && font.fontName.style === DEFAULT_FONT.style);
  return fallback ? fallback.fontName : DEFAULT_FONT;
}

async function getAvailableFonts() {
  if (!availableFontsPromise) {
    availableFontsPromise = figma.listAvailableFontsAsync();
  }
  return await availableFontsPromise;
}

function mapUnityFontToFigma(fontName) {
  const value = String(fontName || '').toLowerCase();
  if (value === 'uifont') {
    return 'uifont_zh-Hans';
  }
  if (value === 'uifont_num') {
    return 'uifont_title';
  }
  if (value === 'uifont_title' || value === 'uifont_title_special' || value === 'uifont_title+special') {
    return 'uifont_title_zh-Hans';
  }
  return fontName || '';
}

function mapUnityFontSizeToFigma(fontSize) {
  const size = positiveNumber(fontSize) || 14;
  return Math.max(1, size);
}

function applyTextAlignment(text, alignment) {
  const value = String(alignment || '').toLowerCase();
  if (value.indexOf('left') >= 0) {
    text.textAlignHorizontal = 'LEFT';
  } else if (value.indexOf('right') >= 0) {
    text.textAlignHorizontal = 'RIGHT';
  } else {
    text.textAlignHorizontal = 'CENTER';
  }

  if (value.indexOf('upper') >= 0 || value.indexOf('top') >= 0) {
    text.textAlignVertical = 'TOP';
  } else if (value.indexOf('lower') >= 0 || value.indexOf('bottom') >= 0) {
    text.textAlignVertical = 'BOTTOM';
  } else {
    text.textAlignVertical = 'CENTER';
  }
}

function parseColor(value) {
  const text = String(value || '#00000000').replace('#', '');
  const r = parseInt(text.slice(0, 2) || '00', 16) / 255;
  const g = parseInt(text.slice(2, 4) || '00', 16) / 255;
  const b = parseInt(text.slice(4, 6) || '00', 16) / 255;
  const a = text.length >= 8 ? parseInt(text.slice(6, 8), 16) / 255 : 1;
  return { r, g, b, a };
}

function positiveNumber(value) {
  const number = Math.abs(Number(value || 0));
  return Number.isFinite(number) && number > 0 ? number : 0;
}

function numberOr(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

function lerp(a, b, t) {
  return a + (b - a) * t;
}

function nearly(a, b) {
  return Math.abs(a - b) < 0.0001;
}

function normalizeRotation(value) {
  let number = Number(value || 0);
  while (number > 180) number -= 360;
  while (number < -180) number += 360;
  return number;
}

