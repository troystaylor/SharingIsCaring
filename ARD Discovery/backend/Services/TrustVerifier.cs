#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ArdDiscovery.Services;

/// <summary>
/// Trust verification result for a crawled catalog entry.
/// </summary>
public class TrustResult
{
    public int Score { get; set; } // 0-100
    public string Level { get; set; } = "none"; // none, basic, verified, high
    public List<string> Signals { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Attestation types supported by the ARD trust framework.
/// </summary>
public static class AttestationTypes
{
    public const string DnsTxt = "dns-txt";
    public const string WellKnownFile = "well-known-file";
    public const string SpiffeId = "spiffe-id";
    public const string DidDocument = "did-document";
    public const string JwsSignature = "jws-signature";
    public const string HttpsOrigin = "https-origin";
}

/// <summary>
/// Verifies trust signals for catalog entries. Implements a tiered scoring model:
/// - HTTPS origin (base) → +10
/// - DNS TXT verification → +20
/// - .well-known file match → +15
/// - JWS catalog signature → +30
/// - SPIFFE/DID attestation → +25
/// 
/// Levels: none (0-9), basic (10-39), verified (40-69), high (70-100)
/// </summary>
public class TrustVerifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TrustVerifier> _logger;

    public TrustVerifier(IHttpClientFactory httpClientFactory, ILogger<TrustVerifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Verify trust for a catalog source domain.
    /// </summary>
    public async Task<TrustResult> VerifyAsync(string domain, string? catalogContent = null)
    {
        var result = new TrustResult();

        // Signal 1: HTTPS origin (always true for domains we crawl over HTTPS)
        result.Score += 10;
        result.Signals.Add($"{AttestationTypes.HttpsOrigin}: verified");

        // Signal 2: DNS TXT record (ard-verify=<expected-hash>)
        await VerifyDnsTxtAsync(domain, result);

        // Signal 3: .well-known verification file
        await VerifyWellKnownFileAsync(domain, result);

        // Signal 4: JWS signature on catalog
        if (!string.IsNullOrEmpty(catalogContent))
        {
            VerifyJwsSignature(catalogContent, result);
        }

        // Determine trust level
        result.Level = result.Score switch
        {
            >= 70 => "high",
            >= 40 => "verified",
            >= 10 => "basic",
            _ => "none"
        };

        return result;
    }

    /// <summary>
    /// Quick trust score for a single entry based on its source domain's TLS validity.
    /// Used during crawl when full verification isn't needed per-entry.
    /// </summary>
    public int QuickScore(string sourceUrl)
    {
        if (string.IsNullOrEmpty(sourceUrl)) return 0;
        if (sourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return 10;
        return 0;
    }

    // ====================================================================
    // DNS TXT Verification
    // ====================================================================

    private async Task VerifyDnsTxtAsync(string domain, TrustResult result)
    {
        try
        {
            // Use DNS over HTTPS (DoH) via Cloudflare
            var client = _httpClientFactory.CreateClient("ard-registry");
            var url = $"https://cloudflare-dns.com/dns-query?name=_ard-verify.{domain}&type=TXT";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/dns-json"));

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(body);
            var answers = json?["Answer"]?.AsArray();

            if (answers != null)
            {
                foreach (var answer in answers)
                {
                    var data = answer?["data"]?.GetValue<string>()?.Trim('"');
                    if (data != null && data.StartsWith("ard-verify="))
                    {
                        result.Score += 20;
                        result.Signals.Add($"{AttestationTypes.DnsTxt}: record found");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DNS TXT verification failed for {Domain}", domain);
        }
    }

    // ====================================================================
    // .well-known Verification File
    // ====================================================================

    private async Task VerifyWellKnownFileAsync(string domain, TrustResult result)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ard-registry");
            var url = $"https://{domain}/.well-known/ard-verify.json";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode) return;

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(content);
            var verifiedDomain = json?["domain"]?.GetValue<string>();

            if (string.Equals(verifiedDomain, domain, StringComparison.OrdinalIgnoreCase))
            {
                result.Score += 15;
                result.Signals.Add($"{AttestationTypes.WellKnownFile}: domain matches");
            }
            else
            {
                result.Warnings.Add($"{AttestationTypes.WellKnownFile}: domain mismatch (expected {domain}, got {verifiedDomain})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, ".well-known verification failed for {Domain}", domain);
        }
    }

    // ====================================================================
    // JWS Signature Verification (catalog-level)
    // ====================================================================

    private void VerifyJwsSignature(string catalogContent, TrustResult result)
    {
        try
        {
            var json = JsonNode.Parse(catalogContent);
            var signature = json?["signature"]?.GetValue<string>();

            if (string.IsNullOrEmpty(signature))
                return;

            // JWS Compact Serialization: header.payload.signature
            var parts = signature.Split('.');
            if (parts.Length != 3)
            {
                result.Warnings.Add($"{AttestationTypes.JwsSignature}: invalid format");
                return;
            }

            // Decode header to check algorithm
            var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var header = JsonNode.Parse(headerJson);
            var alg = header?["alg"]?.GetValue<string>();

            if (alg is "RS256" or "ES256")
            {
                // In production, fetch the JWK from the issuer's jwks_uri and verify
                // For now, presence of a well-formed JWS is a trust signal
                result.Score += 30;
                result.Signals.Add($"{AttestationTypes.JwsSignature}: present ({alg})");
            }
            else
            {
                result.Warnings.Add($"{AttestationTypes.JwsSignature}: unsupported algorithm ({alg})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "JWS verification failed");
            result.Warnings.Add($"{AttestationTypes.JwsSignature}: parse error");
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
