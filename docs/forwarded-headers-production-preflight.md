# Forwarded headers: production preflight

Production must provide an explicitly verified ingress proxy IP or CIDR. Never use unrestricted trust. The existing deploy workflow currently sets ForwardedHeaders__TrustAnyImmediateProxy=true and must be corrected before release.
