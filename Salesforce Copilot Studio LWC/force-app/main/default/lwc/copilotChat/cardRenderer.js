/**
 * Parses Direct Line attachment objects into a safe, LWC-template-friendly
 * data structure.  Supports Hero Cards, Thumbnail Cards, Sign-In Cards,
 * and Adaptive Cards (TextBlock, Image, FactSet, ColumnSet, Container,
 * ActionSet).  One level of nesting is supported inside ColumnSet / Container.
 */

const CONTENT_TYPES = {
    HERO: 'application/vnd.microsoft.card.hero',
    THUMBNAIL: 'application/vnd.microsoft.card.thumbnail',
    ADAPTIVE: 'application/vnd.microsoft.card.adaptive',
    SIGNIN: 'application/vnd.microsoft.card.signin'
};

let _uid = 0;
function uid() {
    _uid += 1;
    return `crd-${_uid}`;
}

// ── Public API ──────────────────────────────────────────────────────

/**
 * Parse an array of Direct Line attachments into renderable card objects.
 * @param {Array} attachments - activity.attachments from Direct Line
 * @returns {Array} Parsed card objects safe for LWC template iteration
 */
export function parseAttachments(attachments) {
    if (!Array.isArray(attachments)) return [];
    return attachments.map(parseOne).filter(Boolean);
}

// ── Internal Parsers ────────────────────────────────────────────────

function parseOne(att) {
    if (!att?.contentType || !att.content) return null;
    switch (att.contentType) {
        case CONTENT_TYPES.HERO:
            return heroCard(att.content, false);
        case CONTENT_TYPES.THUMBNAIL:
            return heroCard(att.content, true);
        case CONTENT_TYPES.ADAPTIVE:
            return adaptiveCard(att.content);
        case CONTENT_TYPES.SIGNIN:
            return signinCard(att.content);
        default:
            return null;
    }
}

function heroCard(c, thumb) {
    return {
        id: uid(),
        isHeroOrThumbnail: true,
        isAdaptive: false,
        isSignIn: false,
        isThumbnail: thumb,
        title: c.title || '',
        subtitle: c.subtitle || '',
        text: c.text || '',
        hasTitle: !!c.title,
        hasSubtitle: !!c.subtitle,
        hasText: !!c.text,
        images: safeImages(c.images),
        hasImages: Array.isArray(c.images) && c.images.length > 0,
        actions: buildActions(c.buttons),
        hasActions: Array.isArray(c.buttons) && c.buttons.length > 0
    };
}

function adaptiveCard(c) {
    return {
        id: uid(),
        isHeroOrThumbnail: false,
        isAdaptive: true,
        isSignIn: false,
        body: parseBody(c.body),
        hasBody: Array.isArray(c.body) && c.body.length > 0,
        actions: buildActions(c.actions),
        hasActions: Array.isArray(c.actions) && c.actions.length > 0
    };
}

function signinCard(c) {
    return {
        id: uid(),
        isHeroOrThumbnail: false,
        isAdaptive: false,
        isSignIn: true,
        text: c.text || 'Please sign in',
        hasText: true,
        actions: buildActions(c.buttons),
        hasActions: Array.isArray(c.buttons) && c.buttons.length > 0
    };
}

// ── Adaptive Card Body ──────────────────────────────────────────────

function parseBody(items) {
    if (!Array.isArray(items)) return [];
    return items.map(parseElement).filter(Boolean);
}

function parseElement(el) {
    if (!el?.type) return null;
    const base = {
        id: uid(),
        isTextBlock: el.type === 'TextBlock',
        isImage: el.type === 'Image',
        isFactSet: el.type === 'FactSet',
        isColumnSet: el.type === 'ColumnSet',
        isContainer: el.type === 'Container',
        isActionSet: el.type === 'ActionSet'
    };

    switch (el.type) {
        case 'TextBlock':
            return {
                ...base,
                text: el.text || '',
                sizeClass: textSize(el.size),
                isBolder: el.weight === 'Bolder',
                isSubtle: el.isSubtle === true,
                wrap: el.wrap !== false
            };
        case 'Image':
            return {
                ...base,
                url: sanitizeUrl(el.url),
                alt: el.altText || 'Image',
                sizeClass: imgSize(el.size)
            };
        case 'FactSet':
            return {
                ...base,
                facts: (el.facts || []).map((f, i) => ({
                    id: `f-${i}`,
                    title: f.title || '',
                    value: f.value || ''
                }))
            };
        case 'ColumnSet':
            return {
                ...base,
                columns: (el.columns || []).map((col, i) => ({
                    id: `cl-${i}`,
                    colClass: `ac-column ${colStyleClass(col.width)}`,
                    items: parseBody(col.items)
                }))
            };
        case 'Container':
            return { ...base, items: parseBody(el.items) };
        case 'ActionSet':
            return { ...base, actions: buildActions(el.actions) };
        default:
            return el.text
                ? {
                      ...base,
                      isTextBlock: true,
                      text: el.text,
                      sizeClass: 'ac-text-default',
                      isBolder: false,
                      isSubtle: false,
                      wrap: true
                  }
                : null;
    }
}

// ── Actions ─────────────────────────────────────────────────────────

function buildActions(list) {
    if (!Array.isArray(list)) return [];
    return list.map((a, i) => {
        const t = a.type || '';
        return {
            id: `act-${i}`,
            title: a.title || a.text || 'Action',
            value: String(a.value ?? a.url ?? a.data ?? ''),
            isOpenUrl: t === 'Action.OpenUrl' || t === 'openUrl',
            isSubmit: t === 'Action.Submit' || t === 'imBack' || t === 'postBack'
        };
    });
}

// ── Helpers ──────────────────────────────────────────────────────────

function safeImages(imgs) {
    if (!Array.isArray(imgs)) return [];
    return imgs.map((img, i) => ({
        id: `im-${i}`,
        url: sanitizeUrl(img.url),
        alt: img.alt || 'Card image'
    }));
}

function sanitizeUrl(url) {
    if (!url) return '';
    try {
        const u = new URL(url);
        if (u.protocol === 'https:' || u.protocol === 'http:') return u.href;
    } catch (_) {
        /* invalid URL */
    }
    return '';
}

function textSize(s) {
    const sizes = {
        small: 'ac-text-small',
        medium: 'ac-text-medium',
        large: 'ac-text-large',
        extralarge: 'ac-text-xlarge'
    };
    return sizes[(s || '').toLowerCase()] || 'ac-text-default';
}

function imgSize(s) {
    const sizes = {
        small: 'ac-image-small',
        medium: 'ac-image-medium',
        large: 'ac-image-large'
    };
    return sizes[(s || '').toLowerCase()] || 'ac-image-auto';
}

function colStyleClass(w) {
    if (!w || w === 'stretch') return 'ac-col-stretch';
    if (w === 'auto') return 'ac-col-auto';
    if (/^\d+$/.test(String(w))) return `ac-col-w${w}`;
    return 'ac-col-stretch';
}
