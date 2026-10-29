// --- CONFIGURAZIONE E AUTENTICAZIONE ---
// The API serves the marketplace, so it is always same-origin (Node on 8000, .NET on 8001).
// Only a page opened from disk (file://) has no API origin of its own.
const baseUrl = window.location.protocol === 'file:' ? 'http://localhost:8000' : window.location.origin;
const myApi = `${baseUrl}/api`;
const token = localStorage.getItem("token") || localStorage.getItem("artaround_token");
const CONTENT_PLACEHOLDER = "/uploads/placeholders/template-no-image.jpg";

const loggedInUsername = localStorage.getItem("username") || marketplaceT("common.you");

if (!token) window.location.href = "login.html";

// 1. LEGGIAMO I DATI DALL'URL (Arrivano dal Content fisso)
const urlParams = new URLSearchParams(window.location.search);
let activeItemId = urlParams.get('itemId'); 
let viewedItemId = activeItemId;
const sourcePage = urlParams.get('source') || 'my-items';
const museumId = urlParams.get('museumId');
const museumName = urlParams.get('museumName') || marketplaceT('itemEditor.unknownMuseum');
const visitId = urlParams.get('visitId');
const urlTitle = urlParams.get('title') || marketplaceT('itemEditor.unknownTitle');
const urlAuthor = urlParams.get('author') || marketplaceT('common.authorUnknown');
const urlImage = urlParams.get('image');

const urlContentId = urlParams.get('contentId');

// VARIABILI GLOBALI
let originalContentId = urlContentId || null;
let museumContentsCache = [];
let dropdownTranslation = { key: 'itemEditor.chooseIntro', options: {} };
let dropdownItemSummary = null;

function setDropdownTranslation(key, options = {}) {
    dropdownItemSummary = null;
    dropdownTranslation = { key, options };
    document.getElementById('dropdown-selected-text').textContent = marketplaceT(key, options);
}

function targetAudienceLabel(audience) {
    const keyByAudience = {
        general: 'itemEditor.audienceGeneral',
        children: 'itemEditor.audienceChildren',
        student: 'itemEditor.audienceStudents',
        expert: 'itemEditor.audienceExperts',
        tourist: 'itemEditor.audienceTourists'
    };
    return keyByAudience[audience] ? marketplaceT(keyByAudience[audience]) : audience;
}

function setDropdownItemSummary(creator, targetAudience) {
    dropdownItemSummary = { creator, targetAudience };
    dropdownTranslation = {
        key: 'itemEditor.itemSummary',
        options: {
            creator,
            target: targetAudienceLabel(targetAudience)
        }
    };
    document.getElementById('dropdown-selected-text').textContent = marketplaceT(
        dropdownTranslation.key,
        dropdownTranslation.options
    );
}

function resolveAssetUrl(path) {
    if (!path) return "";
    return path.startsWith('http') ? path : `${baseUrl}${path.startsWith('/') ? '' : '/'}${path}`;
}

function getVisitBuilderUrl() {
    if (sourcePage === 'my-items') {
        const params = new URLSearchParams();
        if (originalContentId) params.set('contentId', originalContentId);
        return `my_items.html${params.toString() ? `?${params.toString()}` : ''}`;
    }

    const params = new URLSearchParams({
        museumId: museumId,
        museumName: museumName,
        resume: '1'
    });

    if (visitId) params.set('visitId', visitId);
    return `create_visits.html?${params.toString()}`;
}

// --- SISTEMA DI TOAST NOTIFICATIONS ---
function showToast(message, type = "success") {
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        container.setAttribute('role', 'status');
        container.setAttribute('aria-live', 'polite');
        container.setAttribute('aria-atomic', 'true');
        document.body.appendChild(container);
    }
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    const copy = document.createElement('span');
    copy.textContent = message;
    toast.appendChild(copy);
    
    container.appendChild(toast);
    setTimeout(() => toast.classList.add('show'), 10);
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 400); 
    }, 3500);
}

// --- MAPPATURA UI <-> DATABASE ---
const audienceMap = [
    { id: 'children', tone: 'easy', labelKey: 'itemEditor.tagChildren' },
    { id: 'standard', tone: 'medium', labelKey: 'itemEditor.tagStandard' },
    { id: 'expert', tone: 'complex', labelKey: 'itemEditor.tagExperts' }
];

// --- GUIDA ALLA SCRITTURA: placeholder su misura per tono ---
// Un testo per tono (item_texts ha PK (item_id, tone)). Ognuna delle 3 caselle
// riceve un suggerimento diverso — lessico atteso, ingombro indicativo in parole
// e il titolo dell'opera aperta —
// al posto dello stesso placeholder generico ripetuto ovunque. Bilingue: la
// lingua dell'interfaccia (window.marketplaceI18n) sceglie quale dizionario
// usare, cosi' il suggerimento resta nella stessa lingua del resto della UI.
const TONE_WRITING_GUIDE = {
    it: {
        children: { lexicon: 'lessico semplice e frasi brevi', voice: 'un tono curioso e giocoso, come lo spiegheresti a un bambino' },
        standard: { lexicon: 'lessico comune e scorrevole', voice: 'un tono chiaro e accessibile, adatto al grande pubblico' },
        expert: { lexicon: 'lessico specialistico', voice: 'approfondimenti tecnici, storico-critici o materici per un pubblico esperto' }
    },
    en: {
        children: { lexicon: 'simple vocabulary and short sentences', voice: 'a curious, playful tone, as if explaining it to a child' },
        standard: { lexicon: 'common, flowing vocabulary', voice: 'a clear, accessible tone suited to a general audience' },
        expert: { lexicon: 'specialist vocabulary', voice: 'technical, art-historical or material-focused depth for an expert audience' }
    }
};

function getUiLanguage() {
    const lang = window.marketplaceI18n?.getLanguage?.() || document.documentElement.lang || 'it';
    return TONE_WRITING_GUIDE[lang] ? lang : 'it';
}

// Il testo è letto da un vocalizzatore (TTS): ~150 parole/min, quindi 65-80
// parole sono circa 30 secondi di ascolto.
function buildTextPlaceholder(audienceId) {
    const lang = getUiLanguage();
    const tone = TONE_WRITING_GUIDE[lang][audienceId];
    if (!tone) return '';
    return lang === 'en'
        ? `Here you can describe "${urlTitle}" using ${tone.lexicon}: ${tone.voice}. Aim for 65-80 words (about 30 seconds read aloud). \nRemember to save the item before leaving the page.`
        : `Qui puoi descrivere "${urlTitle}" con ${tone.lexicon}: ${tone.voice}. Punta a 65-80 parole (circa 30 secondi di ascolto). \nRicorda di salvare l'item prima di cambiare pagina.`;
}

function applyTailoredPlaceholders() {
    audienceMap.forEach(aud => {
        const textarea = getAudienceTextarea(aud.id);
        if (textarea) textarea.placeholder = buildTextPlaceholder(aud.id);
    });
}

// La lingua dell'interfaccia può cambiare a runtime (selettore nell'Account):
// quando succede, rigeneriamo i placeholder nella nuova lingua invece di
// lasciare quelli tradotti staticamente dal passaggio i18n generico.
window.addEventListener('marketplace:language-changed', applyTailoredPlaceholders);

function getAudienceTextarea(audienceId) {
    return document.getElementById(`text-${audienceId}`);
}

function labelAudienceTextareas() {
    audienceMap.forEach((audience) => {
        const textarea = getAudienceTextarea(audience.id);
        if (!textarea) return;
        textarea.removeAttribute('role');
        textarea.setAttribute('aria-label', marketplaceT(audience.labelKey));
    });
}

document.addEventListener('DOMContentLoaded', async () => {
    // Popoliamo la grafica fissa dell'opera
    setDropdownTranslation('itemEditor.chooseIntro');
    document.getElementById('artwork-title-display').textContent = `${urlTitle} - ${urlAuthor}`;
    document.getElementById('museum-name-display').textContent = museumName;
    applyTailoredPlaceholders();
    labelAudienceTextareas();
    resetCommercialFields();
    await loadAssociatedContentOptions();

    const imgContainer = document.getElementById('image-preview');
    const imagePath = urlImage || CONTENT_PLACEHOLDER;
    const img = document.createElement('img');
    img.src = resolveAssetUrl(imagePath);
    img.alt = urlTitle;
    img.classList.add('image-preview-img');
    imgContainer.innerHTML = '';
    imgContainer.appendChild(img);

    // Logica Accordion (Freccette)
    audienceMap.forEach(aud => {
        const header = document.querySelector(`.audience-header[data-aud="${aud.id}"]`);
        const panel = document.getElementById(`audience-panel-${aud.id}`);
        const arrow = document.getElementById(`arrow-${aud.id}`);
        
        if(header && panel && arrow) {
            header.addEventListener('click', () => {
                const isOpen = !panel.classList.toggle('hidden');
                arrow.classList.toggle('open', isOpen);
                header.setAttribute('aria-expanded', String(isOpen));
            });
        }
    });

    if (activeItemId) {
        await caricaTestiDaDB(activeItemId);
    }

    // Carichiamo le varianti della community solo per QUESTO contentId
    if (originalContentId) {
        loadCommunityItems();
    } else {
        document.getElementById('existing-items-list').innerHTML = `<li class="muted-list-message">${marketplaceT('itemEditor.saveForVersions')}</li>`;
    }

    document.getElementById('btn-cancel-exit').addEventListener('click', () => {
        window.location.href = getVisitBuilderUrl();
    });
});

// --- MENU A TENDINA E RICERCA COMMUNITY ---
const dropdownTrigger = document.getElementById('dropdown-trigger');
const dropdownMenu = document.getElementById('dropdown-menu');
const existingItemsList = document.getElementById('existing-items-list');

function setDropdownOpen(open) {
    dropdownMenu.classList.toggle('hidden', !open);
    dropdownTrigger?.setAttribute('aria-expanded', String(open));
}

if (dropdownTrigger) {
    dropdownTrigger.addEventListener('click', () => {
        setDropdownOpen(dropdownMenu.classList.contains('hidden'));
    });
}

async function loadCommunityItems() {
    if (!originalContentId) return; 

    try {
        const res = await fetch(`${myApi}/items?contentId=${originalContentId}`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!res.ok) return; 
        
        const responseData = await res.json();
        let communityItems = responseData.data || responseData.items || responseData;

        existingItemsList.innerHTML = '';

        if (!Array.isArray(communityItems) || communityItems.length === 0) {
            existingItemsList.innerHTML = `<li class="muted-list-message">${marketplaceT('itemEditor.noVariants')}</li>`;
            return;
        }

        const authorCounts = {};

        communityItems.forEach(item => {
            const li = document.createElement('li');
            
            let creatorName = marketplaceT('itemEditor.anonymousUser');
            if (item.creatorId && item.creatorId.username) creatorName = item.creatorId.username;
            else if (typeof item.creatorId === 'string') creatorName = marketplaceT('itemEditor.user', {
                id: `${item.creatorId.substring(0, 5)}...`
            });

            if (!authorCounts[creatorName]) authorCounts[creatorName] = 0;
            authorCounts[creatorName]++;
            let displayCreatorName = authorCounts[creatorName] > 1
                ? marketplaceT('itemEditor.variant', {
                    creator: creatorName,
                    count: authorCounts[creatorName]
                })
                : creatorName;

            let tags = [];
            if (item.descriptions && Array.isArray(item.descriptions)) {
                item.descriptions.forEach(desc => {
                    const mappedAudience = audienceMap.find(a => a.tone === desc.tone);
                    if (mappedAudience) tags.push(marketplaceT(mappedAudience.labelKey));
                });
            }
            const tagsText = tags.length > 0 ? tags.join(', ') : marketplaceT('itemEditor.noTexts');
            const priceText = item.isOwned
                ? marketplaceT('itemEditor.owned')
                : item.isPurchased
                    ? marketplaceT('itemEditor.acquired')
                    : `${Number(item.price) || 0} ${marketplaceT('common.credits')}`;
            const targetText = targetAudienceLabel(item.targetAudience || 'general');
            const itemDetails = `${targetText} · ${tagsText} · ${item.license || 'CC-BY'} · ${priceText}`;

            const authorLine = document.createElement('span');
            const authorName = document.createElement('strong');
            authorName.textContent = displayCreatorName;
            authorLine.append(document.createTextNode(marketplaceT('itemEditor.itemByPrefix')), authorName);

            const detailsLine = document.createElement('span');
            detailsLine.classList.add('community-item-tags');
            detailsLine.textContent = itemDetails;
            const itemButton = document.createElement('button');
            itemButton.type = 'button';
            itemButton.classList.add('community-item-button');
            itemButton.append(authorLine, detailsLine);
            const selectCommunityItem = async () => {
                await caricaTestiDaDB(item._id);
                setDropdownOpen(false);
            };
            itemButton.addEventListener('click', selectCommunityItem);
            li.appendChild(itemButton);
            existingItemsList.appendChild(li);
        });
    } catch (e) {
        console.error("Errore fetch community items:", e);
    }
}

document.getElementById('btn-create-new').addEventListener('click', () => {
    clearUI();
    activeItemId = null;
    viewedItemId = null;
    setDropdownTranslation('itemEditor.createOwn');
    document.getElementById('btn-delete-item').classList.add('hidden');
    setSaveButtonState({ owned: true });
    setDropdownOpen(false);
});

// Il tasto Salva sta sempre affiancato ad Annulla, anche quando i testi
// visualizzati non sono i tuoi: i campi restano modificabili (v. sotto),
// quindi serve sempre un modo per salvare cio' che hai scritto. Se l'Item
// non e' tuo, "Salva" non sovrascrive quello altrui: crea una tua nuova
// copia (activeItemId resta null => POST, non PUT).
function setSaveButtonState({ owned }) {
    const saveBtn = document.getElementById('btn-save-exit');
    saveBtn.classList.remove('hidden');
    saveBtn.dataset.i18n = owned ? 'itemEditor.save' : 'itemEditor.saveAsNew';
    saveBtn.textContent = marketplaceT(saveBtn.dataset.i18n);
}

function clearUI() {
    audienceMap.forEach(aud => {
        const header = document.querySelector(`.audience-header[data-aud="${aud.id}"]`);
        const panel = document.getElementById(`audience-panel-${aud.id}`);
        const arrow = document.getElementById(`arrow-${aud.id}`);

        const textarea = getAudienceTextarea(aud.id);
        if (textarea) textarea.value = '';

        if(panel) panel.classList.add('hidden');
        if(header) header.setAttribute('aria-expanded', 'false');
        if(arrow) arrow.classList.remove('open');
    });
    document.getElementById('item-creatore').value = loggedInUsername;
    document.getElementById('btn-purchase-item').classList.add('hidden');
    resetCommercialFields();
    document.querySelectorAll('.associated-content-checkbox').forEach((checkbox) => {
        checkbox.checked = false;
    });
}

function resetCommercialFields() {
    document.getElementById('item-prezzo').value = '0';
    document.getElementById('item-license').value = 'CC-BY';
    document.getElementById('item-is-public').checked = true;
    document.getElementById('item-is-private').checked = false;
    document.getElementById('item-target-audience').value = 'general';
}

async function loadAssociatedContentOptions(selectedIds = []) {
    const container = document.getElementById('associated-contents-list');
    if (!container) return;

    try {
        if (museumContentsCache.length === 0 && museumId) {
            const res = await fetch(`${myApi}/museums/${museumId}/contents`, {
                headers: { 'Authorization': `Bearer ${token}` }
            });
            if (!res.ok) throw new Error(marketplaceT('itemEditor.loadContentError'));
            const data = await res.json();
            museumContentsCache = data.contents || [];
        }

        renderAssociatedContentOptions(selectedIds);
    } catch (error) {
        container.innerHTML = '';
        const message = document.createElement('p');
        message.classList.add('associated-empty-message');
        message.textContent = marketplaceT('itemEditor.contentsUnavailable');
        container.appendChild(message);
    }
}

function renderAssociatedContentOptions(selectedIds = []) {
    const container = document.getElementById('associated-contents-list');
    const selected = new Set(selectedIds.map(String));
    const candidates = museumContentsCache.filter(
        (content) => content.universalId !== originalContentId
    );
    container.innerHTML = '';

    if (candidates.length === 0) {
        const message = document.createElement('p');
        message.classList.add('associated-empty-message');
        message.textContent = marketplaceT('itemEditor.noOptionalContents');
        container.appendChild(message);
        return;
    }

    candidates.forEach((content) => {
        const label = document.createElement('label');
        label.classList.add('associated-content-option');

        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.classList.add('associated-content-checkbox');
        checkbox.value = content._id;
        checkbox.checked = selected.has(String(content._id));

        const name = document.createElement('span');
        name.classList.add('associated-content-name');
        name.textContent = content.name || marketplaceT('common.content');

        const type = document.createElement('span');
        type.classList.add('associated-content-type');
        type.textContent = content.type || '';

        label.append(checkbox, name, type);
        container.appendChild(label);
    });
}

async function caricaTestiDaDB(idToLoad) {
    try {
        const res = await fetch(`${myApi}/items/${idToLoad}`, { 
            headers: { 'Authorization': `Bearer ${token}` } 
        });
        
        if (!res.ok) throw new Error(marketplaceT('itemEditor.loadTextsError'));
        const data = await res.json();
        const currentItem = data.item || data; 

        if (currentItem.contentId) originalContentId = currentItem.contentId;
        clearUI();
        viewedItemId = currentItem._id;
        activeItemId = currentItem.isOwned ? currentItem._id : null;

        document.getElementById('item-prezzo').value = String(currentItem.price ?? 0);
        document.getElementById('item-license').value = currentItem.license || 'CC-BY';
        document.getElementById('item-is-public').checked = Boolean(currentItem.isPublic);
        document.getElementById('item-is-private').checked = !currentItem.isPublic;
        document.getElementById('item-target-audience').value = currentItem.targetAudience || 'general';
        await loadAssociatedContentOptions(
            (currentItem.associatedContents || []).map((content) => content._id || content)
        );

        document.getElementById('btn-delete-item').classList.toggle('hidden', !currentItem.isOwned);
        setSaveButtonState({ owned: currentItem.isOwned });
        document.getElementById('btn-purchase-item').classList.toggle(
            'hidden',
            currentItem.isOwned || currentItem.isPurchased || !currentItem.isPublic
        );
        let creatorName = marketplaceT('itemEditor.unknownUser');
        if (currentItem.creatorId && currentItem.creatorId.username) creatorName = currentItem.creatorId.username;
        else if (typeof currentItem.creatorId === 'string') creatorName = marketplaceT('itemEditor.user', {
            id: currentItem.creatorId.substring(0, 5)
        });
        
        document.getElementById('item-creatore').value = creatorName;
        setDropdownItemSummary(
            creatorName,
            currentItem.targetAudience || marketplaceT('common.notAvailable')
        );

        if (currentItem.descriptions && currentItem.descriptions.length > 0) {
            currentItem.descriptions.forEach(descGroup => {
                const uiMap = audienceMap.find(a => a.tone === descGroup.tone);
                const textarea = uiMap ? getAudienceTextarea(uiMap.id) : null;
                if (textarea && descGroup.text) {
                    textarea.value = descGroup.text;
                    document.getElementById(`audience-panel-${uiMap.id}`)?.classList.remove('hidden');
                    document.querySelector(`.audience-header[data-aud="${uiMap.id}"]`)?.setAttribute('aria-expanded', 'true');
                    document.getElementById(`arrow-${uiMap.id}`)?.classList.add('open');
                }
            });
        }
    } catch (error) {
        console.error("Errore Database:", error);
    }
}

// --- SALVATAGGIO (Crea o Aggiorna) ---
document.getElementById('btn-save-exit').addEventListener('click', async () => {
    
    if (!activeItemId && !originalContentId) {
        showToast(marketplaceT('itemEditor.missingContent'), "error");
        return;
    }

    const finalDescriptions = [];
    // Un testo per tono: { tone, text }.
    audienceMap.forEach(aud => {
        const text = getAudienceTextarea(aud.id)?.value.trim();
        if (text) finalDescriptions.push({ tone: aud.tone, text });
    });

    if (finalDescriptions.length === 0) {
        showToast(marketplaceT('itemEditor.needDescription'), "error");
        return;
    }

    const targetAudience = document.getElementById('item-target-audience').value.trim();
    if (!targetAudience) {
        showToast(marketplaceT('itemEditor.targetRequired'), "error");
        document.getElementById('item-target-audience').focus();
        return;
    }

    // ECCO IL PAYLOAD PULITO: Invia solo le informazioni dell'Item!
    const payload = {
        isPublic: document.getElementById('item-is-public').checked,
        contentId: originalContentId, // Il gancio che collega questo Item all'opera fissa
        targetAudience,
        descriptions: finalDescriptions,
        price: Number(document.getElementById('item-prezzo').value),
        license: document.getElementById('item-license').value,
        associatedContents: Array.from(
            document.querySelectorAll('.associated-content-checkbox:checked')
        ).map((checkbox) => checkbox.value)
    };

    if (!Number.isFinite(payload.price) || payload.price < 0) {
        showToast(marketplaceT('itemEditor.invalidPrice'), "error");
        return;
    }

    try {
        const method = activeItemId ? 'PUT' : 'POST';
        const endpoint = activeItemId ? `${myApi}/items/${activeItemId}` : `${myApi}/items`;

        const res = await fetch(endpoint, {
            method: method,
            headers: { 
                'Content-Type': 'application/json', 
                'Authorization': `Bearer ${token}` 
            },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            showToast(marketplaceT('itemEditor.saved'), "success");
            setTimeout(() => {
                window.location.href = getVisitBuilderUrl();
            }, 1200);
        } else {
            const errorData = await res.json().catch(() => ({}));
            showToast(marketplaceT('itemEditor.validationError'), "error");
        }
    } catch (error) {
        showToast(marketplaceT('itemEditor.networkError'), "error");
    }
});

document.getElementById('btn-purchase-item').addEventListener('click', async () => {
    if (!viewedItemId) return;

    try {
        const res = await fetch(`${myApi}/items/${viewedItemId}/purchase`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${token}` }
        });
        const responseData = await res.json().catch(() => ({}));
        if (!res.ok) {
            showToast(marketplaceT(
                responseData.error === 'Insufficient balance'
                    ? 'itemEditor.insufficientBalance'
                    : 'itemEditor.purchaseFailed'
            ), "error");
            return;
        }

        document.getElementById('btn-purchase-item').classList.add('hidden');
        showToast(
            marketplaceT('itemEditor.purchaseSuccess', { balance: responseData.walletBalance }),
            "success"
        );
        await caricaTestiDaDB(viewedItemId);
        await loadCommunityItems();
    } catch (error) {
        showToast(marketplaceT('itemEditor.purchaseConnectionError'), "error");
    }
});

// --- ELIMINAZIONE ITEM ---
document.getElementById('btn-delete-item').addEventListener('click', async () => {
    if (!activeItemId) return;

    if (!confirm(marketplaceT('itemEditor.deleteConfirm'))) {
        return;
    }

    try {
        const res = await fetch(`${myApi}/items/${activeItemId}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${token}` }
        });

        if (res.ok) {
            showToast(marketplaceT('itemEditor.deleted'), "success");
            activeItemId = null; 
            document.getElementById('btn-delete-item').classList.add('hidden'); 
            clearUI(); 
            loadCommunityItems(); 
            setDropdownTranslation('itemEditor.createOwn');
        } else {
            const errorData = await res.json();
            showToast(marketplaceT(
                errorData.error === 'Items used by a visit cannot be deleted'
                    ? 'itemEditor.deleteInUse'
                    : 'itemEditor.deleteUnauthorized'
            ), "error");
        }
    } catch (error) {
        showToast(marketplaceT('itemEditor.deleteConnectionError'), "error");
    }
});

window.addEventListener('marketplace:language-changed', () => {
    if (dropdownItemSummary) {
        setDropdownItemSummary(
            dropdownItemSummary.creator,
            dropdownItemSummary.targetAudience
        );
    } else {
        setDropdownTranslation(dropdownTranslation.key, dropdownTranslation.options);
    }
    labelAudienceTextareas();
    if (originalContentId) loadCommunityItems();
});
