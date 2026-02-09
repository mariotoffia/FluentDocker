using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
    /// <summary>
    /// A recorded request captured by <see cref="MockDockerApiConnection"/>.
    /// </summary>
    public sealed record CapturedRequest(string Method, string Path, string Body);

    /// <summary>
    /// In-memory mock of <see cref="IDockerApiConnection"/> that returns canned
    /// responses and records every request for later verification.
    /// </summary>
    public sealed class MockDockerApiConnection : IDockerApiConnection
    {
        private readonly record struct ResponseEntry(
            string Method,
            string PathContains,
            HttpStatusCode StatusCode,
            string JsonBody,
            string StreamContent,
            byte[] StreamBytes);

        private readonly List<ResponseEntry> _entries = new();
        private readonly List<CapturedRequest> _requests = new();
        private bool _pingSuccess = true;

        public string ApiVersion { get; set; } = "1.45";

        // ── Setup (fluent) ──────────────────────────────────────────────

        public MockDockerApiConnection SetupGet(
            string pathContains, int statusCode, string jsonBody)
        {
            _entries.Add(new ResponseEntry(
                "GET", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null));
            return this;
        }

        public MockDockerApiConnection SetupPost(
            string pathContains, int statusCode, string jsonBody)
        {
            _entries.Add(new ResponseEntry(
                "POST", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null));
            return this;
        }

        public MockDockerApiConnection SetupPut(
            string pathContains, int statusCode, string jsonBody)
        {
            _entries.Add(new ResponseEntry(
                "PUT", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null));
            return this;
        }

        public MockDockerApiConnection SetupDelete(
            string pathContains, int statusCode, string jsonBody)
        {
            _entries.Add(new ResponseEntry(
                "DELETE", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null));
            return this;
        }

        public MockDockerApiConnection SetupStream(
            string pathContains, string streamContent)
        {
            _entries.Add(new ResponseEntry(
                "STREAM", pathContains, HttpStatusCode.OK, null, streamContent, null));
            return this;
        }

        /// <summary>
        /// Sets up a stream endpoint that returns raw binary content.
        /// Useful for testing multiplexed frame parsing.
        /// </summary>
        public MockDockerApiConnection SetupStreamBytes(
            string pathContains, byte[] bytes)
        {
            _entries.Add(new ResponseEntry(
                "STREAM", pathContains, HttpStatusCode.OK, null, null, bytes));
            return this;
        }

        public MockDockerApiConnection SetupPing(bool success)
        {
            _pingSuccess = success;
            return this;
        }

        // ── Verification ────────────────────────────────────────────────

        /// <summary>Returns every request captured so far, in order.</summary>
        public IReadOnlyList<CapturedRequest> GetRequests() => _requests;

        // ── IDockerApiConnection ────────────────────────────────────────

        public Task<HttpResponseMessage> GetAsync(
            string path, CancellationToken ct = default)
        {
            Record("GET", path, null);
            return Task.FromResult(Resolve("GET", path));
        }

        public async Task<HttpResponseMessage> PostAsync(
            string path, HttpContent content = null, CancellationToken ct = default)
        {
            var body = content is not null
                ? await content.ReadAsStringAsync(ct)
                : null;

            Record("POST", path, body);
            return Resolve("POST", path);
        }

        public async Task<HttpResponseMessage> PutAsync(
            string path, HttpContent content, CancellationToken ct = default)
        {
            var body = content is not null
                ? await content.ReadAsStringAsync(ct)
                : null;

            Record("PUT", path, body);
            return Resolve("PUT", path);
        }

        public Task<HttpResponseMessage> DeleteAsync(
            string path, CancellationToken ct = default)
        {
            Record("DELETE", path, null);
            return Task.FromResult(Resolve("DELETE", path));
        }

        public Task<Stream> GetStreamAsync(
            string path, CancellationToken ct = default)
        {
            Record("GET_STREAM", path, null);
            return Task.FromResult(ResolveStream(path));
        }

        public Task<Stream> PostStreamAsync(
            string path, HttpContent content = null, CancellationToken ct = default)
        {
            Record("POST_STREAM", path, null);
            return Task.FromResult(ResolveStream(path));
        }

        public Task<bool> PingAsync(CancellationToken ct = default)
        {
            Record("PING", "/_ping", null);
            return Task.FromResult(_pingSuccess);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // ── Internals ───────────────────────────────────────────────────

        private void Record(string method, string path, string body)
        {
            _requests.Add(new CapturedRequest(method, path, body));
        }

        private HttpResponseMessage Resolve(string method, string path)
        {
            var entry = _entries
                .Where(e => e.Method == method && path.Contains(e.PathContains))
                .LastOrDefault();

            if (entry == default)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        $"{{\"message\":\"no mock for {method} {path}\"}}",
                        Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(entry.StatusCode)
            {
                Content = new StringContent(
                    entry.JsonBody ?? "{}", Encoding.UTF8, "application/json")
            };
        }

        private Stream ResolveStream(string path)
        {
            var entry = _entries
                .Where(e => e.Method == "STREAM" && path.Contains(e.PathContains))
                .LastOrDefault();

            if (entry != default && entry.StreamBytes != null)
                return new MemoryStream(entry.StreamBytes);

            var text = entry == default ? string.Empty : entry.StreamContent ?? string.Empty;
            return new MemoryStream(Encoding.UTF8.GetBytes(text));
        }
    }
}
