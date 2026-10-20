using System;
using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Networking;

namespace Mangos.Ttb.Commerce
{
    // Add to the owning player NetworkObject in BOTH server and client builds.
    // No browser identity, order state, model URL or paid flag authorizes equipment.
    [DisallowMultipleComponent]
    public sealed class TtbGameRelay : NetworkBehaviour
    {
        private readonly SyncVar<string> equippedModel = new SyncVar<string>("");
        private string apiBase, serverId, serverKey, nonce, pendingRequest, pendingEntitlement;
        private Grant lease;
        private float challengeExpires, leaseExpires, renewAt, requestAfter;
        private int generation;
        private bool subscribed, requestActive, ready;
        private GameObject equipment;

        [Serializable] private sealed class Consume { public string ticket, serverId, challenge, entitlementId; }
        [Serializable] private sealed class LeaseInput { public string serverId, shopperId, sessionId, entitlementId; }
        [Serializable] private sealed class Grant
        {
            public bool owned; public string entitlementId, shopperId, sessionId, productId, variantId, modelKey, validUntil;
            public int unitIndex;
        }
        private void Awake() => equippedModel.OnChange += ModelChanged;
        public override void OnStartServer()
        {
            base.OnStartServer();
            requestActive = false;
#if UNITY_SERVER || UNITY_EDITOR
            // Secrets are read at server runtime; never serialize them into Resources, a prefab or WebGL.
            var configured = Environment.GetEnvironmentVariable("MANGOS_TTB_GAME_API");
            serverId = Environment.GetEnvironmentVariable("MANGOS_TTB_GAME_ID");
            serverKey = Environment.GetEnvironmentVariable("MANGOS_TTB_GAME_KEY");
            if (TryApiBase(configured, out var pinned) && Regex.IsMatch(serverId ?? "", "^[A-Za-z0-9_-]{1,64}$") && serverKey?.Length >= 32)
            { apiBase = pinned; ready = true; }
#endif
        }
        public override void OnStartClient() { base.OnStartClient(); SetOwnerSubscriptions(); ModelChanged("", equippedModel.Value, false); }
        public override void OnOwnershipClient(NetworkConnection previous) { base.OnOwnershipClient(previous); SetOwnerSubscriptions(); }
        public override void OnOwnershipServer(NetworkConnection previous) { base.OnOwnershipServer(previous); ClearGrant(); }
        public override void OnStopClient() { if (subscribed) MangoTtbCommerceBridge.NotifyGameReady(false); Unsubscribe(); if (equipment != null) Destroy(equipment); base.OnStopClient(); }
        public override void OnStopServer() { ClearGrant(); StopAllCoroutines(); serverKey = null; ready = false; base.OnStopServer(); }
        private void SetOwnerSubscriptions()
        {
            Unsubscribe(); if (!IsOwner) return;
            subscribed = true;
            MangoTtbCommerceBridge.EntitlementUseRequested += RequestUse;
            MangoTtbCommerceBridge.GameTicketReceived += ReceiveTicket;
            MangoTtbCommerceBridge.GameProbeRequested += Probe;
            MangoTtbCommerceBridge.GameSessionCleared += ClearSession;
            Probe();
        }
        private void Unsubscribe()
        {
            if (!subscribed) return; subscribed = false;
            MangoTtbCommerceBridge.EntitlementUseRequested -= RequestUse;
            MangoTtbCommerceBridge.GameTicketReceived -= ReceiveTicket;
            MangoTtbCommerceBridge.GameProbeRequested -= Probe;
            MangoTtbCommerceBridge.GameSessionCleared -= ClearSession;
        }
        private void Probe() { if (IsOwner) RequestReady(); }
        private void RequestUse(string entitlement) { if (IsOwner && Guid.TryParse(entitlement, out _)) BeginUse(entitlement); }
        private void ReceiveTicket(string request, string entitlement, string ticket) { if (IsOwner) SubmitTicket(request, entitlement, ticket); }
        private void ClearSession() { if (IsOwner) ClearForOwner(); }
        [ServerRpc] private void RequestReady() => ReadyForOwner(Owner, ready);
        [TargetRpc] private void ReadyForOwner(NetworkConnection connection, bool available) => MangoTtbCommerceBridge.NotifyGameReady(available);
        [ServerRpc] private void ClearForOwner() { ClearGrant(); ResultForOwner(Owner, "removed"); }
        [ServerRpc] private void BeginUse(string entitlement)
        {
            if (!ready) { ResultForOwner(Owner, "unavailable"); return; }
            if (requestActive || Time.unscaledTime < requestAfter || !Guid.TryParse(entitlement, out _)) return;
            requestAfter = Time.unscaledTime + 1;
            var bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            nonce = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            pendingRequest = Guid.NewGuid().ToString("N"); pendingEntitlement = entitlement; challengeExpires = Time.unscaledTime + 45;
            ChallengeForOwner(Owner, pendingRequest, entitlement, serverId, nonce);
        }
        [TargetRpc] private void ChallengeForOwner(NetworkConnection connection, string request, string entitlement, string server, string challenge) => MangoTtbCommerceBridge.RequestGameTicket(request, entitlement, server, challenge);
        [ServerRpc] private void SubmitTicket(string request, string entitlement, string ticket)
        {
            if (!ready || requestActive || nonce == null || Time.unscaledTime >= challengeExpires || request != pendingRequest || entitlement != pendingEntitlement ||
                !Regex.IsMatch(ticket ?? "", "^[a-f0-9]{64}$")) { ResultForOwner(Owner, "denied"); return; }
            var input = new Consume { ticket = ticket, serverId = serverId, challenge = nonce, entitlementId = entitlement };
            nonce = pendingRequest = pendingEntitlement = null; requestActive = true;
            StartCoroutine(Authorize(input, generation));
        }
        private IEnumerator Authorize(Consume input, int version)
        {
            Grant result = null;
            yield return Send("/game/tickets/consume", JsonUtility.ToJson(input), value => result = value);
            if (version != generation) { requestActive = false; yield break; }
            requestActive = false;
            if (!Accept(result, input.entitlementId)) { ResultForOwner(Owner, "denied"); yield break; }
            ResultForOwner(Owner, "equipped");
        }
        private void Update()
        {
            if (lease == null) return;
            if (Time.unscaledTime >= leaseExpires) { ClearGrant(); ResultForOwner(Owner, "removed"); return; }
            if (!requestActive && Time.unscaledTime >= renewAt) { requestActive = true; StartCoroutine(Renew(generation)); }
        }
        private IEnumerator Renew(int version)
        {
            var expected = lease.entitlementId; var input = new LeaseInput { serverId = serverId, shopperId = lease.shopperId, sessionId = lease.sessionId, entitlementId = expected };
            Grant result = null; yield return Send("/game/entitlements/lease", JsonUtility.ToJson(input), value => result = value);
            if (version != generation) { requestActive = false; yield break; }
            requestActive = false;
            if (!Accept(result, expected)) { ClearGrant(); ResultForOwner(Owner, "removed"); }
        }
        private bool Accept(Grant result, string expected)
        {
            if (result == null || !result.owned || result.entitlementId != expected || !Guid.TryParse(result.shopperId, out _) || !Guid.TryParse(result.sessionId, out _) ||
                !TtbGameModelRegistry.Contains(result.modelKey) || !DateTimeOffset.TryParse(result.validUntil, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)) return false;
            var seconds = (float)(expiry - DateTimeOffset.UtcNow).TotalSeconds;
            if (seconds <= 0 || seconds > 16) return false;
            lease = result; leaseExpires = Time.unscaledTime + Mathf.Min(seconds, 15); renewAt = Time.unscaledTime + Mathf.Min(8, seconds * .5f);
            equippedModel.Value = result.modelKey; return true;
        }
        private IEnumerator Send(string path, string json, Action<Grant> callback)
        {
            using (var request = new UnityWebRequest(apiBase + path, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 5;
                request.SetRequestHeader("Content-Type", "application/json"); request.SetRequestHeader("Authorization", "Bearer " + serverKey);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > 16384) { callback(null); yield break; }
                Grant grant = null; try { grant = JsonUtility.FromJson<Grant>(request.downloadHandler.text); } catch (ArgumentException) { }
                callback(grant);
            }
        }
        private void ClearGrant() { generation++; nonce = pendingRequest = pendingEntitlement = null; lease = null; equippedModel.Value = ""; }
        [TargetRpc] private void ResultForOwner(NetworkConnection connection, string result) => MangoTtbCommerceBridge.NotifyGameResult(result);
        private void ModelChanged(string previous, string next, bool asServer)
        {
#if !UNITY_SERVER
            if (equipment != null) Destroy(equipment); equipment = null;
            if (!string.IsNullOrEmpty(next)) equipment = TtbGameModelRegistry.Equip(next, transform);
#endif
        }
        private static bool TryApiBase(string configured, out string result)
        {
            return TtbGameApiProfile.TryResolve(configured,
                Environment.GetEnvironmentVariable("MANGOS_TTB_GAME_LOCAL") == "true",
                Environment.GetEnvironmentVariable("MANGOS_TTB_GAME_ENVIRONMENT"), out result);
        }
    }
}
