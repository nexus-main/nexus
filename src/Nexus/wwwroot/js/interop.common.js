export function saveSetting(key, value) {
    if (window.localStorage)
        localStorage.setItem(key, JSON.stringify(value));
}

export function clearSetting(key) {
    if (window.localStorage)
        localStorage.removeItem(key);
}

export function loadSetting(key) {

    if (window.localStorage)
        return JSON.parse(localStorage.getItem(key));

    else
        return null;
}