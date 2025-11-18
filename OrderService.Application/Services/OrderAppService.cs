using OrderService.Application.DTOs;
using OrderService.Application.Interfaces;
using OrderService.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace OrderService.Application.Services
{
    public class OrderAppService
    {
        private readonly IOrderRepository _repo;
        
        public OrderAppService (IOrderRepository repo)
        {
            _repo = repo; 
        }

        public async Task<int> CreateOrderAsync(CreateOrderDto dto)
        {
            if (dto.TotalAmount <= 0)
                throw new Exception("Order amount must be greater than zero");

            var order = new Order
            {
                CustomerName = dto.CustomerName,
                TotalAmount = dto.TotalAmount,
                Status = "Created",
                CreatedAt = DateTime.UtcNow
            };

            return await _repo.CreateAsync(order);
        }

        public Task<Order?> GetByIdAsync(int id)
        {
            return _repo.GetByIdAsync(id);
        }
    }
}
