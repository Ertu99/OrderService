using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.DTOs;
using OrderService.Application.Services;

namespace OrderService.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly OrderAppService _service;

        public OrderController(OrderAppService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateOrderDto dto)
        {
            var id = await _service.CreateOrderAsync(dto);
            return Ok(new {OrderId = id});
        }

        [HttpGet("{id}")]
        public async Task<IActionResult>GetById(int id)
        {
            var order = await _service.GetByIdAsync(id);
            if(order == null)
                return NotFound();

            return Ok(order);
        }
    }
}
