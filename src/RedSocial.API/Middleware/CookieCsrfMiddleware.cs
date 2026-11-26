using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using RedSocial.API.Services;

namespace RedSocial.API.Middleware;

/// <summary>
/// Valida un token antifalsificación en peticiones mutables autenticadas por cookie.
/// Las sesiones Bearer no necesitan esta validación porque el token no se adjunta
/// automáticamente por el navegador.
/// </summary>
public sealed class CookieCsrfMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthCookieSettings _authCookie;

    public CookieCsrfMiddleware(RequestDelegate next, AuthCookieSettings authCookie)
    {
        _next = next;
        _authCookie = authCookie;
    }

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var endpoint = context.GetEndpoint();
        var esAnonimo = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        var requiereAutorizacion = !esAnonimo && endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null;
        var metodoSeguro = HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method)
            || HttpMethods.IsTrace(context.Request.Method);
        var esHub = context.Request.Path.StartsWithSegments("/hubs/chat");
        var usaCookieDeSesion = context.Request.Cookies.ContainsKey(_authCookie.Name);

        if (_authCookie.Enabled && requiereAutorizacion && !metodoSeguro && !esHub && usaCookieDeSesion)
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    exito = false,
                    mensaje = "La sesión de seguridad expiró. Recarga la página e inténtalo de nuevo."
                });
                return;
            }
        }

        await _next(context);
    }
}
