using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedSocial.API.Services;
using RedSocial.Domain.Entities;
using RedSocial.Infrastructure.Persistence;

namespace RedSocial.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly PresenceTracker _presence;

    public ChatController(AppDbContext context, IConfiguration configuration, PresenceTracker presence)
    {
        _context = context;
        _configuration = configuration;
        _presence = presence;
    }

    [HttpGet("contactos")]
    public async Task<IActionResult> Contactos(CancellationToken ct)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return Unauthorized();

        var ids = await _context.Seguidores.AsNoTracking()
            .Where(s => s.IdSeguidor == idUsuario)
            .Select(s => s.IdSeguido)
            .Distinct()
            .ToListAsync(ct);

        var usuarios = await _context.Usuarios.AsNoTracking()
            .Where(u => ids.Contains(u.IdUsuario) && u.Activo)
            .Select(u => new
            {
                idUsuario = u.IdUsuario,
                nombre = u.Perfil != null ? u.Perfil.NombrePerfil : u.NombrePerfil,
                usuario = "@" + u.NombreUsuario,
                fotoPerfilUrl = u.Perfil != null ? u.Perfil.FotoPerfilUrl : null
            })
            .Take(40)
            .ToListAsync(ct);

        var contactos = usuarios
            .Select(u => new { u.idUsuario, u.nombre, u.usuario, u.fotoPerfilUrl, enLinea = _presence.EstaEnLinea(u.idUsuario) })
            .OrderByDescending(u => u.enLinea)
            .ThenBy(u => u.nombre)
            .ToList();

        return Ok(new { exito = true, datos = contactos });
    }

    [HttpGet("conversaciones")]
    public async Task<IActionResult> Conversaciones(CancellationToken ct)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return Unauthorized();

        var conversaciones = await (
            from conversacion in _context.Conversaciones.AsNoTracking()
            let idOtro = conversacion.IdUsuarioA == idUsuario ? conversacion.IdUsuarioB : conversacion.IdUsuarioA
            join usuario in _context.Usuarios.AsNoTracking() on idOtro equals usuario.IdUsuario
            join perfil in _context.PerfilesUsuario.AsNoTracking() on usuario.IdUsuario equals perfil.IdUsuario into perfiles
            from perfil in perfiles.DefaultIfEmpty()
            where conversacion.IdUsuarioA == idUsuario || conversacion.IdUsuarioB == idUsuario
            orderby conversacion.FechaActualizacion descending
            select new
            {
                hash = conversacion.HashConversacion,
                idUsuario = usuario.IdUsuario,
                nombre = perfil != null ? perfil.NombrePerfil : usuario.NombrePerfil,
                usuario = "@" + usuario.NombreUsuario,
                fotoPerfilUrl = perfil != null ? perfil.FotoPerfilUrl : null,
                ultimo = _context.MensajesChat
                    .Where(m => m.IdConversacion == conversacion.IdConversacion)
                    .OrderByDescending(m => m.FechaEnvio)
                    .Select(m => m.Texto)
                    .FirstOrDefault(),
                fecha = conversacion.FechaActualizacion
            }).Take(30).ToListAsync(ct);

        return Ok(new { exito = true, datos = conversaciones });
    }

    [HttpGet("no-leidos")]
    public async Task<IActionResult> NoLeidos(CancellationToken ct)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return Unauthorized();

        var total = await (
            from mensaje in _context.MensajesChat.AsNoTracking()
            join conversacion in _context.Conversaciones.AsNoTracking()
                on mensaje.IdConversacion equals conversacion.IdConversacion
            where !mensaje.Leido
                && mensaje.IdEmisor != idUsuario
                && (conversacion.IdUsuarioA == idUsuario || conversacion.IdUsuarioB == idUsuario)
            select mensaje.IdMensaje
        ).CountAsync(ct);

        return Ok(new { exito = true, datos = new { total } });
    }

    [HttpPost("conversaciones/{idOtro:int}")]
    public async Task<IActionResult> CrearOAbrir(int idOtro, CancellationToken ct)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return Unauthorized();
        if (idUsuario == idOtro) return BadRequest(new { exito = false, mensaje = "No puedes abrir chat contigo mismo." });

        var sigueAlDestinatario = await _context.Seguidores.AnyAsync(s =>
            s.IdSeguidor == idUsuario && s.IdSeguido == idOtro, ct);
        if (!sigueAlDestinatario)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                exito = false,
                mensaje = "Solo puedes escribirle a personas que sigues."
            });

        var (a, b) = ChatSecurity.OrdenarUsuarios(idUsuario.Value, idOtro);
        var hash = ChatSecurity.CrearHash(a, b, _configuration["Jwt:Key"] ?? string.Empty);
        var conversacion = await _context.Conversaciones.FirstOrDefaultAsync(c => c.HashConversacion == hash, ct);
        if (conversacion is null)
        {
            conversacion = new Conversacion
            {
                IdUsuarioA = a,
                IdUsuarioB = b,
                HashConversacion = hash,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };
            await _context.Conversaciones.AddAsync(conversacion, ct);
            await _context.SaveChangesAsync(ct);
        }

        return Ok(new { exito = true, datos = new { hash = conversacion.HashConversacion } });
    }

    [HttpGet("conversaciones/{hash}/mensajes")]
    public async Task<IActionResult> Mensajes(string hash, CancellationToken ct)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return Unauthorized();

        var conversacion = await _context.Conversaciones.AsNoTracking()
            .FirstOrDefaultAsync(c => c.HashConversacion == hash && (c.IdUsuarioA == idUsuario || c.IdUsuarioB == idUsuario), ct);
        if (conversacion is null) return NotFound(new { exito = false, mensaje = "Conversación no encontrada." });

        var mensajes = await _context.MensajesChat.AsNoTracking()
            .Where(m => m.IdConversacion == conversacion.IdConversacion)
            .OrderBy(m => m.FechaEnvio)
            .Take(80)
            .Select(m => new { idMensaje = m.IdMensaje, idEmisor = m.IdEmisor, texto = m.Texto, fechaEnvio = m.FechaEnvio })
            .ToListAsync(ct);

        return Ok(new { exito = true, datos = mensajes });
    }

    [HttpPost("conversaciones/{hash}/leer")]
    public async Task<IActionResult> MarcarLeidos(string hash, CancellationToken ct)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return Unauthorized();

        var conversacion = await _context.Conversaciones
            .FirstOrDefaultAsync(c => c.HashConversacion == hash && (c.IdUsuarioA == idUsuario || c.IdUsuarioB == idUsuario), ct);
        if (conversacion is null) return NotFound(new { exito = false, mensaje = "Conversación no encontrada." });

        var mensajes = await _context.MensajesChat
            .Where(m => m.IdConversacion == conversacion.IdConversacion && !m.Leido && m.IdEmisor != idUsuario)
            .ToListAsync(ct);

        if (mensajes.Count > 0)
        {
            mensajes.ForEach(mensaje => mensaje.Leido = true);
            await _context.SaveChangesAsync(ct);
        }

        return Ok(new { exito = true, datos = new { marcados = mensajes.Count } });
    }

    private int? ObtenerIdUsuario()
    {
        var idClaim = User.FindFirst("sub")?.Value;
        return int.TryParse(idClaim, out var idUsuario) ? idUsuario : null;
    }
}
