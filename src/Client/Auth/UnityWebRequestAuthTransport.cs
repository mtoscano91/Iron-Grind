using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace IronGrind.Client.Auth
{
    /// <summary>
    /// Real transport and retry scheduler: one <see cref="UnityWebRequest"/> per call, run in a coroutine.
    /// Uses default certificate validation; never assigns a certificate handler and follows no redirects (ADR-016).
    /// Every call reports exactly once: if the host is inactive, a post fails at once with
    /// <see cref="AuthHttpOutcome.TransportError"/> and a wait runs its callback at once; when the host is disabled
    /// or destroyed, in-flight posts are aborted and reported as <see cref="AuthHttpOutcome.TransportError"/> and
    /// pending waits run their callbacks (a login retry then fails at once, ending as ServerError).
    /// </summary>
    public sealed class UnityWebRequestAuthTransport : MonoBehaviour, IAuthHttpTransport, IAuthRetryScheduler
    {
        private const string CONTENT_TYPE = "application/octet-stream";

        private static readonly AuthHttpResult TransportFailure = new AuthHttpResult(AuthHttpOutcome.TransportError, 0, null);

        private sealed class PendingPost
        {
            public UnityWebRequest Request;
            public Action<AuthHttpResult> OnDone;
        }

        private sealed class PendingWait
        {
            public Action Callback;
        }

        private readonly List<PendingPost> _posts = new List<PendingPost>();
        private readonly List<PendingWait> _waits = new List<PendingWait>();
        private bool _shuttingDown;

        /// <inheritdoc />
        public void Post(string url, byte[] body, int timeoutSeconds, Action<AuthHttpResult> onDone)
        {
            if (_shuttingDown || !isActiveAndEnabled)
            {
                onDone(TransportFailure);
                return;
            }

            var pending = new PendingPost { Request = CreateRequest(url, body, timeoutSeconds), OnDone = onDone };
            _posts.Add(pending);
            StartCoroutine(PostRoutine(pending));
        }

        /// <inheritdoc />
        public void Schedule(int delaySeconds, Action callback)
        {
            if (_shuttingDown || !isActiveAndEnabled)
            {
                callback();
                return;
            }

            var pending = new PendingWait { Callback = callback };
            _waits.Add(pending);
            StartCoroutine(ScheduleRoutine(delaySeconds, pending));
        }

        private void OnEnable()
        {
            _shuttingDown = false;
        }

        // Called on disable, on GameObject deactivation and before destruction. Disabling only the component
        // does not stop its coroutines, so they are stopped here before the pending calls are completed.
        private void OnDisable()
        {
            _shuttingDown = true;
            StopAllCoroutines();

            PendingPost[] posts = _posts.ToArray();
            PendingWait[] waits = _waits.ToArray();
            _posts.Clear();
            _waits.Clear();

            foreach (PendingPost post in posts)
            {
                post.Request.Abort();
                post.Request.Dispose();
                Complete(() => post.OnDone(TransportFailure));
            }

            foreach (PendingWait wait in waits)
            {
                Complete(wait.Callback);
            }
        }

        private static UnityWebRequest CreateRequest(string url, byte[] body, int timeoutSeconds)
        {
            return new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body) { contentType = CONTENT_TYPE },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
                // A redirect could lead to plain http; with no redirects followed it ends as an error (ServerError).
                redirectLimit = 0
            };
        }

        private IEnumerator PostRoutine(PendingPost pending)
        {
            yield return pending.Request.SendWebRequest();

            if (!_posts.Remove(pending))
            {
                yield break; // already completed by OnDisable
            }

            AuthHttpResult result = ToResult(pending.Request);
            pending.Request.Dispose();
            pending.OnDone(result);
        }

        // Unity 6.3 has no dedicated timeout value in UnityWebRequest.Result: a timeout surfaces as
        // ConnectionError. It is reported as TransportError (not by matching the error text); the
        // client maps both to ServerError, so behaviour is the same.
        private static AuthHttpResult ToResult(UnityWebRequest request)
        {
            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    return new AuthHttpResult(AuthHttpOutcome.Ok, request.responseCode, request.downloadHandler.data);
                case UnityWebRequest.Result.ProtocolError:
                    return new AuthHttpResult(AuthHttpOutcome.HttpError, request.responseCode, request.downloadHandler.data);
                default:
                    return TransportFailure;
            }
        }

        private IEnumerator ScheduleRoutine(int delaySeconds, PendingWait pending)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);

            if (_waits.Remove(pending))
            {
                pending.Callback();
            }
        }

        // One failing callback must not stop the others from being completed.
        private static void Complete(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
