using OrderService.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrderService.Application.Interfaces
{
    public interface IOrderRepository
    {
        Task<int> CreateAsync(Order order);
        Task<Order?> GetByIdAsync(int id);
    }
}
