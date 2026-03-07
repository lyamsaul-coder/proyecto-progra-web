using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;

namespace Proyecto_Progra_WebBlockbuster.API.Services;

public interface IRoomService
{

    Task<List<RoomDto>> GetAllRooms(string? genre = null);

    Task<RoomDto?> GetRoomsById(string movieId);

    Task<Room> CreateRoom(Movie movie, string adminId);

    Task<Room> UpdateRoom(string movieId, Movie movie, string adminId);

    Task DeleteRoom(string movieId, string adminId);

    Task<List<RoomDto>> SearchRoom(string searchTerm);

}