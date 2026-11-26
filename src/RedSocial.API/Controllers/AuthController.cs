using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RedSocial.Application.DTOs.Auth;
using RedSocial.API.Services;
using RedSocial.Application.Interfaces.Services;

namespace RedSocial.API.Controllers;

// El controller es delgado: solo traduce HTTP <-> caso de uso.
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    private readonly AccessLogService _accessLog;
    private readonly AuthCookieSettings _authCookie;
    private readonly IAntiforgery _antiforgery;

    public AuthController(
        IAuthService authService,
        AccessLogService accessLog,
        IOptions<AuthCookieSettings> authCookie,
        IAntiforgery antiforgery)
    {
        _authService = authService;
        _accessLog = accessLog;
        _authCookie = authCookie.Value;
        _antiforgery = antiforgery;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.LoginAsync(dto, ct);
        await _accessLog.RegistrarAsync(dto.UsuarioOEmail, "login", resultado,
            HttpContext.Connection.RemoteIpAddress, ct);
        if (resultado.Exito)
        {
            EstablecerCookie(resultado.Datos);
            OcultarTokenSiCookieActiva(resultado.Datos);
        }
        return resultado.Exito ? Ok(resultado) : Unauthorized(resultado);
    }

    [HttpPost("cuenta/reactivar")]
    [AllowAnonymous]
    public async Task<IActionResult> ReactivarCuenta([FromBody] LoginRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.ReactivarCuentaAsync(dto, ct);
        await _accessLog.RegistrarAsync(dto.UsuarioOEmail, "reactivacion", resultado,
            HttpContext.Connection.RemoteIpAddress, ct);
        if (resultado.Exito)
        {
            EstablecerCookie(resultado.Datos);
            OcultarTokenSiCookieActiva(resultado.Datos);
        }
        return resultado.Exito ? Ok(resultado) : Unauthorized(resultado);
    }

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { exito = true, datos = new { token = tokens.RequestToken } });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(_authCookie.Name, new CookieOptions
        {
            Path = _authCookie.Path,
            Domain = string.IsNullOrWhiteSpace(_authCookie.Domain) ? null : _authCookie.Domain,
            Secure = _authCookie.Secure,
            SameSite = _authCookie.GetSameSiteMode(),
            HttpOnly = true
        });
        return NoContent();
    }

    [HttpPost("registro/solicitar-codigo")]
    [AllowAnonymous]
    [EnableRateLimiting("Registro")]
    public async Task<IActionResult> SolicitarCodigo([FromBody] SolicitarCodigoEmailRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.SolicitarCodigoEmailAsync(dto, ct);
        return resultado.Exito ? Ok(resultado) : Conflict(resultado);
    }

    [HttpPost("registro/verificar-codigo")]
    [AllowAnonymous]
    [EnableRateLimiting("Registro")]
    public async Task<IActionResult> VerificarCodigo([FromBody] VerificarCodigoEmailRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.VerificarCodigoEmailAsync(dto, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("registro/cancelar-codigo")]
    [AllowAnonymous]
    [EnableRateLimiting("Registro")]
    public async Task<IActionResult> CancelarCodigo([FromBody] SolicitarCodigoEmailRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.CancelarCodigoEmailAsync(dto, ct);
        return Ok(resultado);
    }

    [HttpPost("registro")]
    [AllowAnonymous]
    public async Task<IActionResult> Registro([FromBody] RegistroRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.RegistrarAsync(dto, ct);
        return resultado.Exito ? Ok(resultado) : Conflict(resultado);
    }

    [HttpPost("recuperar-password/solicitar-codigo")]
    [AllowAnonymous]
    [EnableRateLimiting("Registro")]
    public async Task<IActionResult> SolicitarRecuperacionPassword([FromBody] SolicitarRecuperacionPasswordRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.SolicitarRecuperacionPasswordAsync(dto, ct);
        return resultado.Exito ? Ok(resultado) : NotFound(resultado);
    }

    [HttpPost("recuperar-password/verificar-codigo")]
    [AllowAnonymous]
    [EnableRateLimiting("Registro")]
    public async Task<IActionResult> VerificarRecuperacionPassword([FromBody] VerificarRecuperacionPasswordRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.VerificarRecuperacionPasswordAsync(dto, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("recuperar-password/cambiar")]
    [AllowAnonymous]
    [EnableRateLimiting("Registro")]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequestDto dto, CancellationToken ct)
    {
        var resultado = await _authService.CambiarPasswordAsync(dto, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("password/cambiar")]
    [Authorize]
    public async Task<IActionResult> CambiarPasswordAutenticado([FromBody] CambiarPasswordAutenticadoRequestDto dto, CancellationToken ct)
    {
        var idClaim = User.FindFirst("sub")?.Value;
        if (!int.TryParse(idClaim, out var idUsuario)) return Unauthorized();

        var resultado = await _authService.CambiarPasswordAutenticadoAsync(idUsuario, dto, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("password/verificar")]
    [Authorize]
    public async Task<IActionResult> VerificarPassword([FromBody] VerificarPasswordRequestDto dto, CancellationToken ct)
    {
        var idClaim = User.FindFirst("sub")?.Value;
        if (!int.TryParse(idClaim, out var idUsuario)) return Unauthorized();

        var resultado = await _authService.VerificarPasswordAsync(idUsuario, dto, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("cuenta/desactivar")]
    [Authorize]
    public async Task<IActionResult> DesactivarCuenta([FromBody] CambiarEstadoCuentaRequestDto dto, CancellationToken ct)
    {
        var idClaim = User.FindFirst("sub")?.Value;
        if (!int.TryParse(idClaim, out var idUsuario)) return Unauthorized();

        var resultado = await _authService.DesactivarCuentaAsync(idUsuario, dto, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("cuenta/eliminar")]
    [Authorize]
    public async Task<IActionResult> EliminarCuenta([FromBody] CambiarEstadoCuentaRequestDto dto, CancellationToken ct)
    {
        var idClaim = User.FindFirst("sub")?.Value;
        if (!int.TryParse(idClaim, out var idUsuario)) return Unauthorized();

        var resultado = await _authService.EliminarCuentaAsync(idUsuario, dto, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpGet("registro/usuario-disponible/{nombreUsuario}")]
    [AllowAnonymous]
    public async Task<IActionResult> UsuarioDisponible([FromRoute] string nombreUsuario, CancellationToken ct)
    {
        var resultado = await _authService.VerificarDisponibilidadUsuarioAsync(nombreUsuario, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpGet("registro/email-disponible")]
    [AllowAnonymous]
    public async Task<IActionResult> EmailDisponible([FromQuery] string email, CancellationToken ct)
    {
        var resultado = await _authService.VerificarDisponibilidadEmailAsync(email, ct);
        return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
    }

    // Endpoint protegido para comprobar que el JWT funciona.
    [HttpGet("perfil")]
    [Authorize]
    public IActionResult Perfil() => Ok(new
    {
        IdUsuario = User.FindFirst("sub")?.Value,
        NombreUsuario = User.Identity?.Name,
        Email = User.FindFirst("email")?.Value,
        Rol = User.FindFirst("rol")?.Value
    });

    [HttpGet("sesion")]
    [Authorize]
    public async Task<IActionResult> Sesion(CancellationToken ct)
    {
        var idClaim = User.FindFirst("sub")?.Value;
        if (!int.TryParse(idClaim, out var idUsuario)) return Unauthorized();

        var resultado = await _authService.ObtenerSesionAsync(idUsuario, ct);
        return resultado.Exito ? Ok(resultado) : Unauthorized(resultado);
    }

    private void EstablecerCookie(AuthResponseDto? datos)
    {
        if (!_authCookie.Enabled || datos is null || string.IsNullOrWhiteSpace(datos.Token)) return;

        Response.Cookies.Append(_authCookie.Name, datos.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = _authCookie.Secure,
            SameSite = _authCookie.GetSameSiteMode(),
            Path = _authCookie.Path,
            Domain = string.IsNullOrWhiteSpace(_authCookie.Domain) ? null : _authCookie.Domain,
            Expires = datos.ExpiraEn
        });
    }

    private void OcultarTokenSiCookieActiva(AuthResponseDto? datos)
    {
        if (!_authCookie.Enabled || datos is null) return;

        datos.Token = string.Empty;
        datos.ExpiraEn = DateTime.MinValue;
    }
}
