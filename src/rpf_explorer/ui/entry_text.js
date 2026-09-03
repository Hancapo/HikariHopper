.pragma library

function escapeMarkup(value) {
    return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
}

function markMatch(name, query, ink) {
    const escapedName = escapeMarkup(name)
    if (!query)
        return escapedName
    const at = name.toLowerCase().indexOf(query.toLowerCase())
    if (at < 0)
        return escapedName
    return escapeMarkup(name.slice(0, at))
        + "<u><font color=\"" + ink + "\"><b>"
        + escapeMarkup(name.slice(at, at + query.length))
        + "</b></font></u>"
        + escapeMarkup(name.slice(at + query.length))
}
