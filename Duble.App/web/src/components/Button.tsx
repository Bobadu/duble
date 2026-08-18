// components/Button.tsx — the one button in the application. The look lives in app.css (.btn and its variants).
import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Icon, type IconName } from './Icon';

type Variant = 'default' | 'primary' | 'ghost' | 'danger';

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'type'> {
  variant?: Variant;
  /** An icon before the label, or the whole button when there are no children. */
  icon?: IconName;
  small?: boolean;
  children?: ReactNode;
}

export function Button({ variant = 'default', icon, small, className, children, ...rest }: ButtonProps) {
  const classes = ['btn'];
  if (variant !== 'default') classes.push(variant);
  if (small) classes.push('sm');
  if (icon && !children) classes.push('icon');
  if (className) classes.push(className);

  return (
    <button type="button" className={classes.join(' ')} {...rest}>
      {icon && <Icon name={icon} />}
      {children}
    </button>
  );
}
