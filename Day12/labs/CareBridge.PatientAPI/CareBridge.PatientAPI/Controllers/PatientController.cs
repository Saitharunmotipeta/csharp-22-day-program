
using CareBridge.PatientAPI.Models;
using CareBridge.PatientAPI.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CareBridge.PatientAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientController : ControllerBase
{
    private readonly PatientRepository _repo;

    public PatientController(PatientRepository repo) => _repo = repo;

    /// <summary>Returns all active patients</summary>
    [HttpGet]
    public IActionResult GetAll() =>
        Ok(_repo.GetAll().Where(p => p.IsActive));

    /// <summary>Returns a single patient by numeric ID</summary>
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id)
    {
        var patient = _repo.GetById(id);
        return patient is null ? NotFound() : Ok(patient);
    }

    /// <summary>Adds a new patient record</summary>
    [HttpPost]
    public IActionResult Create([FromBody] Patient patient) =>
        Created($"/api/patient/{patient.Id}", _repo.Add(patient));
}

