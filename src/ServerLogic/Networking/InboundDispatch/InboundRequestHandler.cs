using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// A request handler (ADR-014 Decision 3). It decodes <paramref name="body"/> with the message's
    /// codec and calls its system. It runs on the tick thread, cannot keep <paramref name="body"/>
    /// after it returns, must not suspend (ADR-011) and must not read <see cref="ICharacterMutationGate"/>.
    /// A custom delegate, because <see cref="ReadOnlySpan{T}"/> cannot be a generic argument of <c>Action&lt;&gt;</c>.
    /// </summary>
    /// <param name="context">What the dispatcher knows about the request.</param>
    /// <param name="body">The bytes after the envelope; valid only during the call.</param>
    /// <example>
    /// <code>
    /// dispatcher.Register(descriptor, (in InboundRequestContext context, ReadOnlySpan&lt;byte&gt; body) => { });
    /// </code>
    /// </example>
    public delegate void InboundRequestHandler(in InboundRequestContext context, ReadOnlySpan<byte> body);
}
