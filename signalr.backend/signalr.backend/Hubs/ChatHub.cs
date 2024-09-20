using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using signalr.backend.Data;
using signalr.backend.Models;

namespace signalr.backend.Hubs
{
    // On garde en mémoire les connexions actives (clé: email, valeur: userId)
    // Note: Ce n'est pas nécessaire dans le TP
    public static class UserHandler
    {
        public static Dictionary<string, string> UserConnections { get; set; } = new Dictionary<string, string>();
    }

    // L'annotation Authorize fonctionne de la même façon avec SignalR qu'avec Web API
    [Authorize]
    // Le Hub est le type de base des "contrôleurs" de SignalR
    public class ChatHub : Hub
    {
        public ApplicationDbContext _context;

        public IdentityUser CurentUser
        {
            get
            {
                // On récupère le userid à partir du Cookie qui devrait être envoyé automatiquement
                string userid = Context.UserIdentifier!;
                return _context.Users.Single(u => u.Id == userid);
            }
        }

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Liste des utilisateurs connectés
        /// </summary>
        /// <returns>Liste des utilisateurs connecté</returns>
        private async Task UserList()
        {
            //All = tous, "UsersList", ce qu'il y a côté client, UserHandler = permet d'avoir la liste des utilisateurs connectés
            await Clients.All.SendAsync("UsersList", UserHandler.UserConnections.ToList());
        }

        public async override Task OnConnectedAsync()
        {
            UserHandler.UserConnections.Add(CurentUser.Email!, Context.UserIdentifier);

            // TODO: Envoyer des messages aux clients pour les mettre à jour
            //liste des utilisateurs
            await UserList();
            //liste des channels
            await Clients.All.SendAsync("ChannelsList", _context.Channel.ToList());
        }

        public async override Task OnDisconnectedAsync(Exception? exception)
        {
            // Lors de la fermeture de la connexion, on met à jour notre dictionnary d'utilisateurs connectés
            KeyValuePair<string, string> entrie = UserHandler.UserConnections.SingleOrDefault(uc => uc.Value == Context.UserIdentifier);
            UserHandler.UserConnections.Remove(entrie.Key);

            // TODO: Envoyer un message aux clients pour les mettre à jour
            await UserList();
        }

        public async Task CreateChannel(string title)
        {
            _context.Channel.Add(new Channel { Title = title });
            await _context.SaveChangesAsync();

            // TODO: Envoyer un message aux clients pour les mettre à jour
            await Clients.All.SendAsync("ChannelsList", _context.Channel.ToListAsync());
        }

        public async Task DeleteChannel(int channelId)
        {
            Channel channel = _context.Channel.Find(channelId);

            if(channel != null)
            {
                _context.Channel.Remove(channel);
                await _context.SaveChangesAsync();
            }
            string groupName = CreateChannelGroupName(channelId);
            // Envoyer les messages nécessaires aux clients

            //envoi d'un message afin d'avertir les utilisateurs que le canal a été détruit
            await Clients.Group(groupName).SendAsync("NewMessage", "[" + channelId + "]" + " a été détruit");
            //cherche du côté client la fonction LeaveChannel
            await Clients.Group(groupName).SendAsync("LeaveChannel");
            //mettre à jour pour tous la liste des canaux existants
            await Clients.All.SendAsync("ChannelsList", _context.Channel.ToList());
        }

        /// <summary>
        /// Permet de se joindre à un canal
        /// </summary>
        /// <param name="oldChannelId">ancien id du canal </param>
        /// <param name="newChannelId">nouvel id du canal</param>
        /// <returns></returns>
        public async Task JoinChannel(int oldChannelId, int newChannelId)
        {
            string userTag = "[" + CurentUser.Email! + "]";

            // TODO: Faire quitter le vieux canal à l'utilisateur

            if (oldChannelId > 0)
            {
                //créer un nom de group avec l'id du canal
                string oldGroupName = CreateChannelGroupName(oldChannelId);
                //trouver le canal
                Channel channel = _context.Channel.Find(oldChannelId);
                //initialiser un message
                string message = userTag + " quitte : " + channel.Title;
                //envoyer le message aux utilisateurs concernés
                await Clients.Group(oldGroupName).SendAsync("NewMessage", message);
                //Gérer le groupe -- retirer du groupe
                //besoin de l'id de la connexion et du nom en string du groupe
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, oldGroupName);
            }
            

            // TODO: Faire joindre le nouveau canal à l'utilisateur
            if(newChannelId > 0)
            {
                string newGroupName = CreateChannelGroupName(newChannelId);
                await Groups.AddToGroupAsync(Context.ConnectionId, newGroupName);

                Channel channel = _context.Channel.Find(newChannelId);
               
                //message
                string message = userTag + " a joint : " + channel.Title;
                await Clients.Group(newGroupName).SendAsync("NewMessage", message);
            }
        }

        public async Task SendMessage(string message, int channelId, string userId)
        {
            if (userId != null)
            {
                // TODO: Envoyer le message à cet utilisateur
                string messageTag = "[De " + CurentUser.Email! + "]" + message;
                await Clients.User(userId).SendAsync("NewMessage", messageTag);
            }
            else if (channelId != 0)
            {
                // TODO: Envoyer le message aux utilisateurs connectés à ce canal
                string groupName = CreateChannelGroupName(channelId);
                Channel channel = _context.Channel.Find(channelId);
                await Clients.Group(groupName).SendAsync("NewMessage", "[" + channel.Title + "]" + message);
            }
            else
            {
                await Clients.All.SendAsync("NewMessage", "[Tous] " + message);
            }
        }

        private static string CreateChannelGroupName(int channelId)
        {
            return "Channel" + channelId;
        }
    }
}