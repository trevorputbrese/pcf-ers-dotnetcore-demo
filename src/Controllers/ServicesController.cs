using System.Collections.Generic;
using System.Threading.Tasks;
using Articulate.Models;
using Articulate.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Articulate.Controllers;

public class ServicesController : Controller
{
    private readonly AttendeeContext _db;

    public ServicesController(AttendeeContext db)
    {
        _db = db;
    }

    public IActionResult Index() => View();
        
    [HttpPost]
    public async Task ClearUsers()
    {
        var attendees = await _db.Attendees.ToListAsync();
        _db.Attendees.RemoveRange(attendees);
        await _db.SaveChangesAsync();
    }

    [HttpPost]
    public async Task AddUser(Attendee attendee)
    {

        _db.Attendees.Add(attendee);
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<Attendee>> GetUsers() => await _db.Attendees.ToListAsync();

    public DbConnectionInfo GetDbConnectionInfo() => new()
    {
        ProviderName = _db.Database.ProviderName, 
        ConnectionString = _db.Database.GetConnectionString()
    };
}