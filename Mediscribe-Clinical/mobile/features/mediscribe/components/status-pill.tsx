import { Text } from '@/components/ui/Text';
import { cn } from '@/lib/cn';
import type { SessionStatus } from '@/features/mediscribe/types';

interface StatusPillProps {
  status: SessionStatus;
}

const statusConfig: Record<SessionStatus, { label: string; className: string }> = {
  pending: {
    label: 'Pending',
    className: 'bg-warning/15 text-warning',
  },
  in_review: {
    label: 'In Review',
    className: 'bg-primary/15 text-primary',
  },
  completed: {
    label: 'Completed',
    className: 'bg-success/15 text-success',
  },
};

export function StatusPill({ status }: StatusPillProps) {
  const config = statusConfig[status];

  return (
    <Text className={cn('px-3 py-1 rounded-full text-xs font-semibold overflow-hidden', config.className)}>
      {config.label}
    </Text>
  );
}
