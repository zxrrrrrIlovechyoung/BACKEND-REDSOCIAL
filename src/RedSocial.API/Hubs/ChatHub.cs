using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RedSocial.API.Services;
using RedSocial.Domain.Entities;
using RedSocial.Infrastructure.Persistence;

namespace RedSocial.API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _context;
    private readonly PresenceTracker _presence;
    private readonly IConfiguration _configuration;

    public ChatHub(AppDbContext context, PresenceTracker presence, IConfiguration configuration)
    {
        _context = context;
        _presence = presence;
        _configuration = configuration;
    }

    public override async Task OnConnectedAsync()
    {
        if (!OrigenPermitido())
        {
            Context.Abort();
            return;
        }

        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is not null)
        {
            _presence.Conectar(idUsuario.Value, Context.ConnectionId);
            await Clients.All.SendAsync("UsuariosEnLinea", _presence.EnLinea());
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is not null)
        {
            _presence.Desconectar(idUsuario.Value, Context.ConnectionId);
            await Clients.All.SendAsync("UsuariosEnLinea", _presence.EnLinea());
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task UnirseConversacion(string hash)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return;

        var conversacion = await _context.Conversaciones.AsNoTracking()
            .FirstOrDefaultAsync(c => c.HashConversacion == hash
                && (c.IdUsuarioA == idUsuario || c.IdUsuarioB == idUsuario));
        if (conversacion is null) return;

        var idDestinatario = conversacion.IdUsuarioA == idUsuario
            ? conversacion.IdUsuarioB
            : conversacion.IdUsuarioA;
        if (!await PuedeEnviarAAsync(idUsuario.Value, idDestinatario)) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, hash);
    }

    public async Task EnviarMensaje(string hash, string texto)
    {
        var idUsuario = ObtenerIdUsuario();
        if (idUsuario is null) return;

        texto = (texto ?? string.Empty).Trim();
        if (texto.Length is < 1 or > 1000) return;

        var conversacion = await _context.Conversaciones
            .FirstOrDefaultAsync(c => c.HashConversacion == hash && (c.IdUsuarioA == idUsuario || c.IdUsuarioB == idUsuario));
        if (conversacion is null) return;

        var idDestinatario = conversacion.IdUsuarioA == idUsuario
            ? conversacion.IdUsuarioB
            : conversacion.IdUsuarioA;
        if (!await PuedeEnviarAAsync(idUsuario.Value, idDestinatario)) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, hash);

        var mensaje = new MensajeChat
        {
            IdConversacion = conversacion.IdConversacion,
            IdEmisor = idUsuario.Value,
            Texto = texto,
            FechaEnvio = DateTime.UtcNow
        };

        conversacion.FechaActualizacion = mensaje.FechaEnvio;
        await _context.MensajesChat.AddAsync(mensaje);
        await _context.SaveChangesAsync();

        await Clients.Group(hash).SendAsync("MensajeRecibido", new
        {
            idMensaje = mensaje.IdMensaje,
            hash,
            idEmisor = mensaje.IdEmisor,
            texto = mensaje.Texto,
            fechaEnvio = mensaje.FechaEnvio
        });
    }

    private int? ObtenerIdUsuario()
    {
        var idClaim = Context.User?.FindFirst("sub")?.Value;
        return int.TryParse(idClaim, out var idUsuario) ? idUsuario : null;
    }

    private bool OrigenPermitido()
    {
        var origen = Context.GetHttpContext()?.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origen)) return true;

        var origenes = _configuration.GetSection("Cors:Origenes").Get<string[]>() ?? [];
        return origenes.Contains(origen, StringComparer.OrdinalIgnoreCase);
    }

    private Task<bool> PuedeEnviarAAsync(int idUsuario, int idDestinatario) =>
        _context.Seguidores.AsNoTracking().AnyAsync(
            s => s.IdSeguidor == idUsuario && s.IdSeguido == idDestinatario,
            Context.ConnectionAborted);
}
