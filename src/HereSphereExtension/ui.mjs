// Plain-ESM HereSphere debug settings bundle for the com.cove.heresphere extension.
// Loaded by the Cove extension runtime (no bundler, no JSX): it imports shared runtime
// modules via the import map and default-exports { components: { HereSphereDebug } }.
// The HereSphereDebug component is rendered in the "page" settings layout at /settings/heresphere.
import * as React from "@cove/runtime/react";
import { extensionFetch } from "@cove/runtime/api";

const styles = {
  root: { display: "flex", flexDirection: "column", gap: 16 },
  intro: { margin: 0, fontSize: 13, opacity: 0.7, lineHeight: 1.55, maxWidth: 900 },
  card: { border: "1px solid rgba(127,127,127,0.22)", borderRadius: 10, padding: "14px 16px", background: "rgba(127,127,127,0.05)" },
  cardTitle: { margin: 0, fontSize: 13, fontWeight: 600, letterSpacing: "0.03em", textTransform: "uppercase", opacity: 0.85 },
  cardSubtitle: { margin: "5px 0 0", fontSize: 12, opacity: 0.6, lineHeight: 1.45 },
  grid: { display: "grid", gridTemplateColumns: "minmax(130px, 240px) 1fr", rowGap: 7, columnGap: 18, marginTop: 12 },
  label: { fontSize: 12, opacity: 0.65, whiteSpace: "nowrap", overflowWrap: "anywhere" },
  value: { fontSize: 13, wordBreak: "break-word" },
  list: { listStyle: "none", margin: "10px 0 0", padding: 0, display: "flex", flexDirection: "column", gap: 8 },
  item: { border: "1px solid rgba(127,127,127,0.16)", borderRadius: 8, padding: "8px 11px", display: "flex", flexDirection: "column", gap: 4 },
  itemTitle: { fontSize: 13, fontWeight: 600 },
  muted: { opacity: 0.65, fontSize: 12, lineHeight: 1.5 },
  groupLabel: { marginTop: 14, fontSize: 12, fontWeight: 600, opacity: 0.75 },
  badge: { display: "inline-block", padding: "2px 10px", borderRadius: 999, fontSize: 12, fontWeight: 600, border: "1px solid rgba(127,127,127,0.25)", background: "rgba(127,127,127,0.08)" },
  badgeOk: { border: "1px solid rgba(34,197,94,0.45)", background: "rgba(34,197,94,0.14)", color: "#16a34a" },
  badgeNo: { border: "1px solid rgba(239,68,68,0.45)", background: "rgba(239,68,68,0.14)", color: "#dc2626" },
  status: { padding: "24px", textAlign: "center", opacity: 0.75, fontSize: 14 },
  error: { padding: "12px 16px", borderRadius: 10, border: "1px solid rgba(239,68,68,0.4)", background: "rgba(239,68,68,0.1)", color: "#dc2626", fontSize: 13, lineHeight: 1.5 },
};

function badge(text, tone) {
  const extra = tone === "ok" ? styles.badgeOk : tone === "no" ? styles.badgeNo : {};
  return React.createElement("span", { style: Object.assign({}, styles.badge, extra) }, text);
}

function fmtDate(value) {
  if (!value) return "\u2014";
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleString();
}

function renderValue(value) {
  if (value === null || value === undefined) return "\u2014";
  if (typeof value === "boolean") return value ? "yes" : "no";
  if (Array.isArray(value)) return value.length ? value.join(", ") : "(none)";
  if (value === "") return "\u2014";
  return String(value);
}

function section(title, subtitle, rows) {
  const children = [
    React.createElement("h3", { key: "title", style: styles.cardTitle }, title),
    React.createElement("p", { key: "subtitle", style: styles.cardSubtitle }, subtitle),
  ];
  if (rows && rows.length) children.push(gridRows(rows));
  return React.createElement("section", { style: styles.card }, children);
}

function gridRows(rows) {
  return React.createElement("div", { style: styles.grid },
    rows.flatMap((row, i) => [
      React.createElement("span", { key: i + "-label", style: styles.label }, row[0]),
      React.createElement("span", { key: i + "-value", style: styles.value }, renderValue(row[1])),
    ])
  );
}

function emptyItem(text) {
  return React.createElement("li", { style: { opacity: 0.5, fontSize: 12 } }, text);
}

function tokenItem(t) {
  return React.createElement("li", { key: t.id, style: styles.item },
    React.createElement("div", { style: styles.itemTitle }, t.name || "(unnamed token)"),
    React.createElement("div", { style: styles.muted },
      "prefix " + (t.prefix || "\u2014") +
      " \u00b7 created " + fmtDate(t.createdAt) +
      " \u00b7 last used " + fmtDate(t.lastUsedAt) +
      " \u00b7 expires " + (t.expiresAt ? fmtDate(t.expiresAt) : "never") +
      (t.scope && t.scope.length ? " \u00b7 scope: " + t.scope.join(", ") : "")
    )
  );
}

function renderResolution(resolution) {
  const r = resolution || {};
  const access = r.access;
  const ok = access === 1;
  const accessText = access === 1 ? "logged-in (access 1)"
    : access === 0 ? "guest (access 0)"
    : access === -1 ? "bad login (access -1)"
    : "unknown (access " + access + ")";
  return React.createElement("section", { style: styles.card },
    React.createElement("h3", { style: styles.cardTitle }, "HSP auth resolution"),
    React.createElement("p", { style: styles.cardSubtitle },
      "What GET /heresphere actually resolves for this request — the same code path as the HSP catalog, scan and video endpoints. Cove auth drives HSP access: a valid auth-token header, the Cove session (cove_access_token cookie), an Authorization/query token, or username/password."),
    React.createElement("div", { style: { marginTop: 12 } }, badge(accessText, ok ? "ok" : "no")),
    gridRows([
      ["resolved access", access],
      ["source", r.source],
      ["resolved principal userId", r.resolvedPrincipalUserId],
      ["resolved principal kind", r.resolvedPrincipalKind],
      ["auth-token returned", r.resolvedTokenPresent],
      ["auth-token header present", r.authTokenHeaderPresent],
      ["Authorization header present", r.standardAuthorizationHeaderPresent],
      ["?access_token= query present", r.queryAccessTokenPresent],
      ["cove_access_token cookie present", r.coveAccessTokenCookiePresent],
    ])
  );
}

function renderAccess(access) {
  const has = !!(access && access.hasHereSphereToken);
  const hereTokens = (access && access.hereSphereTokens) || [];
  const otherTokens = (access && access.otherApiTokens) || [];
  return React.createElement("section", { style: styles.card },
    React.createElement("h3", { style: styles.cardTitle }, "HereSphere access"),
    React.createElement("p", { style: styles.cardSubtitle },
      "The durable API token(s) issued for this user. HereSphere uses its own credentials to call its API; it does not reuse the Cove username/password."),
    React.createElement("div", { style: { marginTop: 12 } }, badge(has ? "HereSphere token: present" : "HereSphere token: missing", has ? "ok" : "no")),
    React.createElement("div", { style: styles.groupLabel }, "HereSphere tokens (" + hereTokens.length + ")"),
    React.createElement("ul", { style: styles.list },
      hereTokens.length ? hereTokens.map(tokenItem) : emptyItem("None issued for this user.")
    ),
    React.createElement("div", { style: styles.groupLabel }, "Other API tokens (" + otherTokens.length + ")"),
    React.createElement("ul", { style: styles.list },
      otherTokens.length ? otherTokens.map(tokenItem) : emptyItem("None.")
    )
  );
}

function ratingItem(r) {
  const title = r.title || (r.hostType + " #" + r.hostId);
  const hsp = r.hspValue === null || r.hspValue === undefined ? "\u2014" : Number(r.hspValue).toFixed(2);
  return React.createElement("li", { key: r.id, style: styles.item },
    React.createElement("div", { style: styles.itemTitle }, title),
    React.createElement("div", { style: styles.muted },
      "aspect " + r.aspect + " \u00b7 host " + r.hostType + " #" + r.hostId +
      " \u00b7 Cove " + r.coveValue + " \u00b7 HSP " + hsp
    )
  );
}

function renderRatings(ratings) {
  const list = ratings || [];
  return React.createElement("section", { style: styles.card },
    React.createElement("h3", { style: styles.cardTitle }, "Ratings"),
    React.createElement("p", { style: styles.cardSubtitle },
      list.length + " rating(s) for this user. Cove scale 0\u2013100; HSP scale 0\u20135 (hsp = cove / 20)."),
    React.createElement("ul", { style: styles.list },
      list.length ? list.map(ratingItem) : emptyItem("No ratings recorded.")
    )
  );
}

function favoriteItem(f) {
  const title = f.title || (f.hostType + " #" + f.hostId);
  return React.createElement("li", { key: f.id, style: styles.item },
    React.createElement("div", { style: styles.itemTitle }, title),
    React.createElement("div", { style: styles.muted },
      "host " + f.hostType + " #" + f.hostId +
      " \u00b7 favorited " + fmtDate(f.favoritedAt) +
      " \u00b7 views " + f.viewCount +
      " \u00b7 completed " + f.completeCount +
      " \u00b7 consumed " + Math.round(f.totalConsumedSec || 0) + "s" +
      (f.lastConsumedAt ? " \u00b7 last consumed " + fmtDate(f.lastConsumedAt) : "")
    )
  );
}

function renderFavorites(favorites) {
  const list = favorites || [];
  return React.createElement("section", { style: styles.card },
    React.createElement("h3", { style: styles.cardTitle }, "Favorites"),
    React.createElement("p", { style: styles.cardSubtitle }, list.length + " favorite(s) for this user."),
    React.createElement("ul", { style: styles.list },
      list.length ? list.map(favoriteItem) : emptyItem("No favorites recorded.")
    )
  );
}

function renderPersistence(persistence) {
  const p = persistence || {};
  return section(
    "HSP write / persistence",
    "Metadata describing how HereSphere persists engagement (ratings, favorites) to Cove for this user.",
    [
      ["can write engagement", p.canWriteEngagement],
      ["engagement service available", p.engagementServiceAvailable],
      ["engagement service", p.engagementServiceType],
      ["rating count", p.ratingCount],
      ["favorite count", p.favoriteCount],
      ["rating scale", p.ratingScale],
      ["favorite field", p.favoriteField],
      ["rating host types", p.ratingHostTypeValues],
      ["affinity host types", p.affinityHostTypeValues],
    ]
  );
}

function renderIdentity(identity) {
  if (!identity) {
    return section("Identity", "No Cove identity was resolved for this request.", [["userId", null]]);
  }
  return section(
    "Identity",
    "The Cove session (principal) used for this request; it is not HereSphere username/password authentication.",
    [
      ["userId", identity.userId],
      ["username", identity.username],
      ["principal kind", identity.kind],
      ["roles", identity.roles],
      ["session token id", identity.tokenId],
      ["ip", identity.ip],
      ["user agent", identity.userAgent],
    ]
  );
}

function HereSphereDebug() {
  const [state, setState] = React.useState({ status: "loading", data: null, error: null });

  React.useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await extensionFetch("/api/heresphere/debug");
        const body = await res.json().catch(() => null);
        if (!res.ok) {
          const message = (body && body.error) || "Request failed with HTTP " + res.status;
          throw new Error(message);
        }
        if (!cancelled) setState({ status: "success", data: body, error: null });
      } catch (err) {
        if (!cancelled) setState({ status: "error", data: null, error: (err && err.message) || String(err) });
      }
    })();
    return () => { cancelled = true; };
  }, []);

  if (state.status === "loading") {
    return React.createElement("div", { style: styles.status }, "Loading HereSphere integration state\u2026");
  }

  if (state.status === "error") {
    return React.createElement("div", { style: styles.error },
      "Unable to load HereSphere debug state: " + state.error
    );
  }

  const data = state.data || {};
  return React.createElement("div", { style: styles.root },
    React.createElement("p", { style: styles.intro },
      "Read-only view of the current Cove user's HereSphere integration: identity/session, live HSP auth resolution, HereSphere access (API token), per-user ratings and favorites, and HSP write/persistence metadata. Nothing on this page mutates live data."),
    renderIdentity(data.identity),
    renderResolution(data.resolution),
    renderAccess(data.access),
    renderRatings(data.ratings),
    renderFavorites(data.favorites),
    renderPersistence(data.persistence)
  );
}

// ---------------------------------------------------------------------------------------------
// Settings: tag category order, segment markers, subtitles, and the user's own API tokens.
// ---------------------------------------------------------------------------------------------

const settingsStyles = {
  row: { display: "flex", alignItems: "center", gap: 8, padding: "6px 10px", border: "1px solid rgba(127,127,127,0.16)", borderRadius: 8 },
  rowNum: { width: 26, fontSize: 12, opacity: 0.6, fontVariantNumeric: "tabular-nums" },
  rowLabel: { flex: 1, fontSize: 13 },
  iconBtn: { border: "1px solid rgba(127,127,127,0.3)", background: "transparent", color: "inherit", borderRadius: 6, width: 26, height: 24, cursor: "pointer", fontSize: 12, lineHeight: "20px", padding: 0 },
  revokeBtn: { border: "1px solid rgba(239,68,68,0.45)", background: "rgba(239,68,68,0.1)", color: "#dc2626", borderRadius: 6, width: 26, height: 24, cursor: "pointer", fontSize: 13, lineHeight: "20px", padding: 0 },
  primaryBtn: { border: "1px solid rgba(127,127,127,0.35)", background: "rgba(127,127,127,0.14)", color: "inherit", borderRadius: 8, padding: "6px 14px", cursor: "pointer", fontSize: 13, fontWeight: 600 },
  field: { display: "flex", alignItems: "center", gap: 10, marginTop: 10, fontSize: 13 },
  select: { background: "transparent", color: "inherit", border: "1px solid rgba(127,127,127,0.35)", borderRadius: 6, padding: "4px 8px", fontSize: 13 },
  actions: { display: "flex", alignItems: "center", gap: 12, marginTop: 14 },
  ok: { fontSize: 12, color: "#16a34a" },
};

async function fetchJson(url, init) {
  const res = await extensionFetch(url, init);
  if (res.status === 204) return null;
  const body = await res.json().catch(() => null);
  if (!res.ok) throw new Error((body && body.error) || "Request failed with HTTP " + res.status);
  return body;
}

function moveItem(list, index, delta) {
  const target = index + delta;
  if (target < 0 || target >= list.length) return list;
  const next = list.slice();
  const [item] = next.splice(index, 1);
  next.splice(target, 0, item);
  return next;
}

function CategoryOrderEditor(props) {
  const { order, labels, disabled, onChange } = props;
  return React.createElement("div", { style: { display: "flex", flexDirection: "column", gap: 6, marginTop: 12 } },
    order.map((key, i) => React.createElement("div", { key, style: settingsStyles.row },
      React.createElement("span", { style: settingsStyles.rowNum }, (i + 1) + "."),
      React.createElement("span", { style: settingsStyles.rowLabel }, labels[key] || key),
      React.createElement("button", {
        type: "button", style: settingsStyles.iconBtn, title: "Move up", "aria-label": "Move " + (labels[key] || key) + " up",
        disabled: disabled || i === 0, onClick: () => onChange(moveItem(order, i, -1)),
      }, "\u25B2"),
      React.createElement("button", {
        type: "button", style: settingsStyles.iconBtn, title: "Move down", "aria-label": "Move " + (labels[key] || key) + " down",
        disabled: disabled || i === order.length - 1, onClick: () => onChange(moveItem(order, i, 1)),
      }, "\u25BC")
    ))
  );
}

function SettingsCard() {
  const [state, setState] = React.useState({ status: "loading", error: null });
  const [draft, setDraft] = React.useState(null);
  const [meta, setMeta] = React.useState({ labels: {}, canEdit: false });
  const [saving, setSaving] = React.useState({ busy: false, message: null, error: null });

  React.useEffect(() => {
    let cancelled = false;
    fetchJson("/api/heresphere/settings")
      .then((body) => {
        if (cancelled) return;
        setDraft(body.settings);
        setMeta({ labels: body.categoryLabels || {}, canEdit: !!body.canEdit && !!body.storeAvailable });
        setState({ status: "ready", error: null });
      })
      .catch((err) => { if (!cancelled) setState({ status: "error", error: err.message || String(err) }); });
    return () => { cancelled = true; };
  }, []);

  async function save() {
    setSaving({ busy: true, message: null, error: null });
    try {
      const body = await fetchJson("/api/heresphere/settings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(draft),
      });
      setDraft(body.settings);
      setSaving({ busy: false, message: "Saved. HereSphere picks this up on its next library refresh.", error: null });
    } catch (err) {
      setSaving({ busy: false, message: null, error: err.message || String(err) });
    }
  }

  const title = React.createElement("h3", { style: styles.cardTitle }, "Settings");
  if (state.status === "loading") return React.createElement("section", { style: styles.card }, title, React.createElement("p", { style: styles.cardSubtitle }, "Loading\u2026"));
  if (state.status === "error") return React.createElement("section", { style: styles.card }, title, React.createElement("div", { style: Object.assign({}, styles.error, { marginTop: 10 }) }, "Unable to load settings: " + state.error));

  const disabled = !meta.canEdit || saving.busy;
  const set = (patch) => setDraft(Object.assign({}, draft, patch));
  return React.createElement("section", { style: styles.card },
    title,
    React.createElement("p", { style: styles.cardSubtitle },
      "Applies to every user. Each tag category is numbered in this order (\u201C1. Tag\u201D, \u201C2. Group\u201D, \u2026), and HereSphere sorts its tag categories by that number." +
      (meta.canEdit ? "" : " Changing these settings needs the extensions.configure permission.")),
    React.createElement("div", { style: styles.groupLabel }, "Tag category order"),
    React.createElement(CategoryOrderEditor, {
      order: draft.tagCategoryOrder || [], labels: meta.labels, disabled,
      onChange: (order) => set({ tagCategoryOrder: order }),
    }),
    React.createElement("label", { style: settingsStyles.field },
      "Segment timeline markers",
      React.createElement("select", {
        style: settingsStyles.select, value: draft.segmentSource, disabled,
        onChange: (e) => set({ segmentSource: e.target.value }),
      },
        React.createElement("option", { value: "user" }, "Segments I created in Cove"),
        React.createElement("option", { value: "all" }, "All segments (including AI / automatic)"),
        React.createElement("option", { value: "none" }, "Off")
      )
    ),
    React.createElement("label", { style: settingsStyles.field },
      React.createElement("input", {
        type: "checkbox", checked: !!draft.subtitlesEnabled, disabled,
        onChange: (e) => set({ subtitlesEnabled: e.target.checked }),
      }),
      "Offer Cove sidecar captions (.srt / .vtt) as HereSphere subtitles"
    ),
    React.createElement("div", { style: settingsStyles.actions },
      React.createElement("button", { type: "button", style: settingsStyles.primaryBtn, disabled, onClick: save }, saving.busy ? "Saving\u2026" : "Save"),
      saving.message ? React.createElement("span", { style: settingsStyles.ok }, saving.message) : null,
      saving.error ? React.createElement("span", { style: { fontSize: 12, color: "#dc2626" } }, saving.error) : null
    )
  );
}

function TokensCard() {
  const [state, setState] = React.useState({ status: "loading", tokens: [], error: null });
  const [revoking, setRevoking] = React.useState(null);

  const load = React.useCallback(async () => {
    try {
      const tokens = await fetchJson("/api/heresphere/tokens");
      setState({ status: "ready", tokens: tokens || [], error: null });
    } catch (err) {
      setState({ status: "error", tokens: [], error: err.message || String(err) });
    }
  }, []);

  React.useEffect(() => { load(); }, [load]);

  async function revoke(token) {
    const what = token.isHereSphere ? "the HereSphere token" : "the token \u201C" + token.name + "\u201D";
    const consequence = token.isHereSphere ? " HereSphere will need to log in again." : " Anything using it will stop working.";
    if (!window.confirm("Revoke " + what + " (" + token.prefix + "\u2026)?" + consequence)) return;
    setRevoking(token.id);
    try {
      await fetchJson("/api/heresphere/tokens/" + encodeURIComponent(token.id), { method: "DELETE" });
      await load();
    } catch (err) {
      setState((s) => Object.assign({}, s, { error: err.message || String(err) }));
    } finally {
      setRevoking(null);
    }
  }

  function tokenRow(t) {
    return React.createElement("li", { key: t.id, style: Object.assign({}, styles.item, { flexDirection: "row", alignItems: "center", gap: 10 }) },
      React.createElement("div", { style: { flex: 1, display: "flex", flexDirection: "column", gap: 4 } },
        React.createElement("div", { style: styles.itemTitle },
          (t.name || "(unnamed token)") + (t.isHereSphere ? " \u00b7 HereSphere" : "") + (t.isCurrent ? " \u00b7 this session" : "")),
        React.createElement("div", { style: styles.muted },
          "prefix " + (t.prefix || "\u2014") +
          " \u00b7 created " + fmtDate(t.createdAt) +
          " \u00b7 last used " + fmtDate(t.lastUsedAt) +
          " \u00b7 expires " + (t.expiresAt ? fmtDate(t.expiresAt) : "never"))
      ),
      React.createElement("button", {
        type: "button", style: settingsStyles.revokeBtn, title: "Revoke this token", "aria-label": "Revoke " + (t.name || "token"),
        disabled: revoking !== null, onClick: () => revoke(t),
      }, revoking === t.id ? "\u2026" : "\u00D7")
    );
  }

  const children = [
    React.createElement("h3", { key: "t", style: styles.cardTitle }, "Your API tokens"),
    React.createElement("p", { key: "s", style: styles.cardSubtitle },
      "Tokens that can act as your Cove account. HereSphere's token never expires; revoke it with \u00D7 if a headset is lost or shared. HereSphere gets a new one the next time it logs in."),
  ];
  if (state.error) children.push(React.createElement("div", { key: "e", style: Object.assign({}, styles.error, { marginTop: 10 }) }, state.error));
  if (state.status === "loading") children.push(React.createElement("p", { key: "l", style: styles.muted }, "Loading\u2026"));
  else children.push(React.createElement("ul", { key: "u", style: styles.list },
    state.tokens.length ? state.tokens.map(tokenRow) : emptyItem("No active API tokens.")));
  return React.createElement("section", { style: styles.card }, children);
}

function HereSphereSettings() {
  return React.createElement("div", { style: styles.root },
    React.createElement(SettingsCard, null),
    React.createElement(TokensCard, null)
  );
}

export default {
  components: {
    HereSphereSettings,
    HereSphereDebug,
  },
};
