# 0003 — No `UseHttpsRedirection` behind the Northflank proxy

**Date:** 2026-04-01 · **Status:** Accepted

## Context

`app.UseHttpsRedirection()` is in the default ASP.NET Core template and is correct for an app that
terminates TLS itself. Fiolyn does not: Northflank terminates TLS at its edge and forwards plain
HTTP to the container, which listens on port 8080 via `ASPNETCORE_URLS=http://+:8080`
(see [`0002`](0002-neon-and-northflank-hosting.md)).

With the middleware enabled, every forwarded request looks insecure to the app, so it answers with
a 307 back to the `https://` URL. The client follows it, the proxy terminates TLS again, forwards
plain HTTP again — and the app redirects again. The result is a redirect loop that presents as the
service being completely unreachable, with nothing obviously wrong in the logs.

## Decision

`UseHttpsRedirection` is never registered. TLS is entirely the proxy's responsibility.

The middleware pipeline in `Program.cs` is exactly:

```
UseExceptionHandler → UseCors → UseAuthentication → UseAuthorization → MapControllers
```

## Consequences

- Running the container directly, without a TLS-terminating proxy in front of it, serves plain
  HTTP. That is correct for this deployment and must not be "fixed" by adding the middleware back.
- Any future host change must either terminate TLS externally too, or this decision needs revisiting
  together with the port binding.
- `UseCors` sits before `UseAuthentication` deliberately — preflight requests are unauthenticated,
  and a CORS failure surfaces as a browser network error rather than a 401, which makes it easy to
  misdiagnose.
